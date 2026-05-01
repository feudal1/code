using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace tools
{
    /// <summary>
    /// Body WL 数据辅助类
    /// </summary>
    public class BodyWLData
    {
        public List<Dictionary<string, int>> WLFreqs { get; set; } = new List<Dictionary<string, int>>();
        public List<LabelData> Labels { get; set; } = new List<LabelData>();
    }

    /// <summary>
    /// 零件 WL 数据辅助类
    /// </summary>
    public class PartWLData
    {
        public List<Dictionary<string, int>> WLFreqs { get; set; } = new List<Dictionary<string, int>>();
        public List<LabelData> Labels { get; set; } = new List<LabelData>();
    }

    /// <summary>
    /// 标注数据辅助类
    /// </summary>
    public class LabelData
    {
        public string Category { get; set; }
        public string Value { get; set; }
        public double Confidence { get; set; }
        public string Notes { get; set; }

        public LabelData(string category, string value, double confidence, string notes)
        {
            Category = category;
            Value = value;
            Confidence = confidence;
            Notes = notes;
        }
    }

    /// <summary>
    /// 零件拓扑图数据库 - 使用 SQLite 存储 WL 图核结果
    /// </summary>
    public class TopologyDatabase
    {
        private readonly string _connectionString;
        private readonly string _dbPath;

        public TopologyDatabase(string dbPath = "topology_labels.db")
        {
            _dbPath = dbPath;
            _connectionString = $"Data Source={dbPath};Version=3;";
            InitializeDatabase();
            UpgradeDatabase(); // 升级数据库表结构
        }

        /// <summary>
        /// 初始化数据库表结构
        /// </summary>
        private void InitializeDatabase()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                // 启用外键约束
                using (var cmd = new SQLiteCommand("PRAGMA foreign_keys = ON;", connection))
                {
                    cmd.ExecuteNonQuery();
                }

                // 创建零件表
                string createPartsTable = @"
                    CREATE TABLE IF NOT EXISTS parts (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        part_name TEXT NOT NULL,
                        file_path TEXT NOT NULL,
                        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                        updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                        UNIQUE(part_name, file_path)
                    );";

                // 创建 body 表（新增）
                string createBodiesTable = @"
                    CREATE TABLE IF NOT EXISTS bodies (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        part_id INTEGER NOT NULL,
                        body_name TEXT NOT NULL,
                        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (part_id) REFERENCES parts(id) ON DELETE CASCADE,
                        UNIQUE(part_id, body_name)
                    );";

                // 创建 face 表（新增，用于 face 级别标注）
                string createFacesTable = @"
                    CREATE TABLE IF NOT EXISTS faces (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        body_id INTEGER NOT NULL,
                        face_index INTEGER NOT NULL,
                        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (body_id) REFERENCES bodies(id) ON DELETE CASCADE,
                        UNIQUE(body_id, face_index)
                    );";

                // 创建 WL 迭代结果表
                string createWLResultsTable = @"
                    CREATE TABLE IF NOT EXISTS wl_results (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        body_id INTEGER NOT NULL,
                        iteration INTEGER NOT NULL,
                        label_frequencies TEXT NOT NULL,
                        FOREIGN KEY (body_id) REFERENCES bodies(id) ON DELETE CASCADE,
                        UNIQUE(body_id, iteration)
                    );";

                // 创建 face WL 结果表（新增）
                string createFaceWLResultsTable = @"
                    CREATE TABLE IF NOT EXISTS face_wl_results (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        face_id INTEGER NOT NULL,
                        iteration INTEGER NOT NULL,
                        label_frequencies TEXT NOT NULL,
                        FOREIGN KEY (face_id) REFERENCES faces(id) ON DELETE CASCADE,
                        UNIQUE(face_id, iteration)
                    );";

                // 创建用户标注表（添加唯一性约束，防止同一个 body 的同一类别有多个标注）
                string createLabelsTable = @"
                    CREATE TABLE IF NOT EXISTS user_labels (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        body_id INTEGER NOT NULL,
                        label_category TEXT NOT NULL,
                        label_value TEXT NOT NULL,
                        confidence REAL DEFAULT 1.0,
                        notes TEXT,
                        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (body_id) REFERENCES bodies(id) ON DELETE CASCADE,
                        UNIQUE(body_id, label_category)
                    );";

                // 创建 face 标注表（新增）
                string createFaceLabelsTable = @"
                    CREATE TABLE IF NOT EXISTS face_user_labels (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        face_id INTEGER NOT NULL,
                        label_category TEXT NOT NULL,
                        label_value TEXT NOT NULL,
                        confidence REAL DEFAULT 1.0,
                        notes TEXT,
                        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (face_id) REFERENCES faces(id) ON DELETE CASCADE,
                        UNIQUE(face_id, label_category)
                    );";

                // 创建索引
                string createIndexes = @"
                    CREATE INDEX IF NOT EXISTS idx_part_name ON parts(part_name);
                    CREATE INDEX IF NOT EXISTS idx_body_part ON bodies(part_id);
                    CREATE INDEX IF NOT EXISTS idx_wl_body ON wl_results(body_id);
                    CREATE INDEX IF NOT EXISTS idx_label_category ON user_labels(label_category);
                    CREATE INDEX IF NOT EXISTS idx_label_value ON user_labels(label_value);
                ";

                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = createPartsTable;
                    command.ExecuteNonQuery();

                    command.CommandText = createBodiesTable;
                    command.ExecuteNonQuery();

                    command.CommandText = createFacesTable;
                    command.ExecuteNonQuery();

                    command.CommandText = createWLResultsTable;
                    command.ExecuteNonQuery();

                    command.CommandText = createFaceWLResultsTable;
                    command.ExecuteNonQuery();

                    command.CommandText = createLabelsTable;
                    command.ExecuteNonQuery();

                    command.CommandText = createFaceLabelsTable;
                    command.ExecuteNonQuery();

                    command.CommandText = createIndexes;
                    command.ExecuteNonQuery();
                }

                Console.WriteLine($"数据库初始化完成：{_dbPath}");
            }
        }

        /// <summary>
        /// 升级数据库表结构（添加唯一性约束）
        /// </summary>
        private void UpgradeDatabase()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                // 检查 user_labels 表是否已有唯一性约束
                string checkConstraint = @"
                    SELECT COUNT(*) FROM sqlite_master 
                    WHERE type='index' AND name='idx_unique_body_category'
                    AND tbl_name='user_labels'";
                
                using (var cmd = new SQLiteCommand(checkConstraint, connection))
                {
                    var result = cmd.ExecuteScalar();
                    bool needsUpgrade = Convert.ToInt32(result) == 0;
                    
                    if (needsUpgrade)
                    {
                        Console.WriteLine("正在升级数据库表结构...");
                        
                        // 先清理重复数据，保留每个 (body_id, label_category) 组合中最新的一条
                        string cleanupDuplicates = @"
                            DELETE FROM user_labels 
                            WHERE id NOT IN (
                                SELECT MAX(id) 
                                FROM user_labels 
                                GROUP BY body_id, label_category
                            )";
                        
                        using (var cmdCleanup = new SQLiteCommand(cleanupDuplicates, connection))
                        {
                            int deletedCount = cmdCleanup.ExecuteNonQuery();
                            if (deletedCount > 0)
                            {
                                Console.WriteLine($"✓ 已清理 {deletedCount} 条重复标注记录");
                            }
                        }
                        
                        // 创建唯一索引
                        string createUniqueIndex = @"
                            CREATE UNIQUE INDEX IF NOT EXISTS idx_unique_body_category 
                            ON user_labels(body_id, label_category)";
                        
                        using (var cmdIndex = new SQLiteCommand(createUniqueIndex, connection))
                        {
                            cmdIndex.ExecuteNonQuery();
                            Console.WriteLine("✓ 已添加唯一性约束：(body_id, label_category)");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 添加或更新零件及其所有 body 的 WL 结果（保存所有迭代轮次）
        /// </summary>
        public List<int> UpsertPartWithBodies(string partName, string filePath, List<BodyGraph> bodyGraphs)
        {
            // 去掉文件名后缀
            string partNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(partName);
            
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // 插入或更新零件信息
                        int partId = GetOrCreatePart(connection, partNameWithoutExtension, filePath);
                        
                        var bodyIds = new List<int>();
                        
                        // 处理每个 body
                        foreach (var bodyGraph in bodyGraphs)
                        {
                            int bodyId = GetOrCreateBody(connection, partId, bodyGraph.FullBodyName);
                            
                            // 删除该 body 旧的 WL 结果
                            string deleteOld = "DELETE FROM wl_results WHERE body_id = @body_id";
                            using (var cmd = new SQLiteCommand(deleteOld, connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@body_id", bodyId);
                                cmd.ExecuteNonQuery();
                            }

                            // 执行 WL 迭代并保存所有轮次的结果
                            var allIterations = WLGraphKernel.PerformWLIterations(bodyGraph, iterations: 1);
                            
                            for (int iter = 0; iter < allIterations.Count; iter++)
                            {
                                string insertWL = @"
                                    INSERT INTO wl_results (body_id, iteration, label_frequencies) 
                                    VALUES (@body_id, @iteration, @frequencies)";

                                using (var cmd = new SQLiteCommand(insertWL, connection, transaction))
                                {
                                    cmd.Parameters.Clear();
                                    cmd.Parameters.AddWithValue("@body_id", bodyId);
                                    cmd.Parameters.AddWithValue("@iteration", iter);
                                    cmd.Parameters.AddWithValue("@frequencies", JsonConvert.SerializeObject(allIterations[iter]));
                                    cmd.ExecuteNonQuery();
                                }
                            }
                            
                            bodyIds.Add(bodyId);
                            Console.WriteLine($"✓ Body '{bodyGraph.FullBodyName}' 已存入数据库 ({bodyGraph.Nodes.Count} 个面)");
                        }

                        transaction.Commit();
                        Console.WriteLine($"✓ 零件 '{partName}' 的 {bodyGraphs.Count} 个 body 已全部存储");
                        return bodyIds;
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        Console.WriteLine($"× 存储零件失败：{ex.Message}");
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// 获取或创建零件记录
        /// </summary>
        private int GetOrCreatePart(SQLiteConnection connection, string partName, string filePath)
        {
            // 先尝试查找
            string select = "SELECT id FROM parts WHERE part_name = @part_name AND file_path = @file_path";
            using (var cmd = new SQLiteCommand(select, connection))
            {
                cmd.Parameters.AddWithValue("@part_name", partName);
                cmd.Parameters.AddWithValue("@file_path", filePath);
                var result = cmd.ExecuteScalar();
                if (result != null)
                {
                    return Convert.ToInt32(result);
                }
            }

            // 不存在则插入
            string insert = @"
                INSERT INTO parts (part_name, file_path) 
                VALUES (@part_name, @file_path)";
            
            using (var cmd = new SQLiteCommand(insert, connection))
            {
                cmd.Parameters.AddWithValue("@part_name", partName);
                cmd.Parameters.AddWithValue("@file_path", filePath);
                cmd.ExecuteNonQuery();
                
                // 返回新插入的 ID
                cmd.CommandText = "SELECT last_insert_rowid()";
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        /// <summary>
        /// 获取或创建 body 记录
        /// </summary>
        private int GetOrCreateBody(SQLiteConnection connection, int partId, string bodyName)
        {
            // 先尝试查找
            string select = "SELECT id FROM bodies WHERE part_id = @part_id AND body_name = @body_name";
            using (var cmd = new SQLiteCommand(select, connection))
            {
                cmd.Parameters.AddWithValue("@part_id", partId);
                cmd.Parameters.AddWithValue("@body_name", bodyName);
                var result = cmd.ExecuteScalar();
                if (result != null)
                {
                    return Convert.ToInt32(result);
                }
            }

            // 不存在则插入
            string insert = @"
                INSERT INTO bodies (part_id, body_name) 
                VALUES (@part_id, @body_name)";
            
            using (var cmd = new SQLiteCommand(insert, connection))
            {
                cmd.Parameters.AddWithValue("@part_id", partId);
                cmd.Parameters.AddWithValue("@body_name", bodyName);
                cmd.ExecuteNonQuery();
                
                // 返回新插入的 ID
                cmd.CommandText = "SELECT last_insert_rowid()";
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        /// <summary>
        /// 根据名称获取或创建 body 记录（公开方法）
        /// </summary>
        public int GetOrCreateBodyByNames(int partId, string bodyName)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                return GetOrCreateBody(connection, partId, bodyName);
            }
        }

        /// <summary>
        /// 获取或创建 face 记录
        /// </summary>
        public int GetOrCreateFace(int bodyId, int faceIndex)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                // 先尝试查找
                string select = "SELECT id FROM faces WHERE body_id = @body_id AND face_index = @face_index";
                using (var cmd = new SQLiteCommand(select, connection))
                {
                    cmd.Parameters.AddWithValue("@body_id", bodyId);
                    cmd.Parameters.AddWithValue("@face_index", faceIndex);
                    var result = cmd.ExecuteScalar();
                    if (result != null)
                    {
                        return Convert.ToInt32(result);
                    }
                }

                // 不存在则插入
                string insert = @"
                    INSERT INTO faces (body_id, face_index) 
                    VALUES (@body_id, @face_index)";
                
                using (var cmd = new SQLiteCommand(insert, connection))
                {
                    cmd.Parameters.AddWithValue("@body_id", bodyId);
                    cmd.Parameters.AddWithValue("@face_index", faceIndex);
                    cmd.ExecuteNonQuery();
                    
                    // 返回新插入的 ID
                    cmd.CommandText = "SELECT last_insert_rowid()";
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
        }

        /// <summary>
        /// 保存 Face 的 WL 结果
        /// </summary>
        public void SaveFaceWLResult(int faceId, int iteration, Dictionary<string, int> frequencies)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                // 删除旧的 WL 结果
                string deleteOld = "DELETE FROM face_wl_results WHERE face_id = @face_id AND iteration = @iteration";
                using (var cmd = new SQLiteCommand(deleteOld, connection))
                {
                    cmd.Parameters.AddWithValue("@face_id", faceId);
                    cmd.Parameters.AddWithValue("@iteration", iteration);
                    cmd.ExecuteNonQuery();
                }
                
                // 插入新的 WL 结果
                string insert = @"
                    INSERT INTO face_wl_results (face_id, iteration, label_frequencies) 
                    VALUES (@face_id, @iteration, @frequencies)";
                
                using (var cmd = new SQLiteCommand(insert, connection))
                {
                    cmd.Parameters.AddWithValue("@face_id", faceId);
                    cmd.Parameters.AddWithValue("@iteration", iteration);
                    cmd.Parameters.AddWithValue("@frequencies", JsonConvert.SerializeObject(frequencies));
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// 添加 Face 标注
        /// </summary>
        public void AddFaceLabel(int faceId, string category, string value, double confidence = 1.0, string? notes = null)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                // 先删除该 category 的旧标注
                string deleteOld = "DELETE FROM face_user_labels WHERE face_id = @face_id AND label_category = @category";
                using (var cmd = new SQLiteCommand(deleteOld, connection))
                {
                    cmd.Parameters.AddWithValue("@face_id", faceId);
                    cmd.Parameters.AddWithValue("@category", category);
                    cmd.ExecuteNonQuery();
                }
                
                // 插入新标注
                string insert = @"
                    INSERT INTO face_user_labels (face_id, label_category, label_value, confidence, notes)
                    VALUES (@face_id, @category, @value, @confidence, @notes)";

                using (var cmd = new SQLiteCommand(insert, connection))
                {
                    cmd.Parameters.AddWithValue("@face_id", faceId);
                    cmd.Parameters.AddWithValue("@category", category);
                    cmd.Parameters.AddWithValue("@value", value);
                    cmd.Parameters.AddWithValue("@confidence", confidence);
                    cmd.Parameters.AddWithValue("@notes", notes ?? (object)DBNull.Value);
                    cmd.ExecuteNonQuery();
                }

                Console.WriteLine($"✓ 已标注：{category} = {value} (置信度：{confidence})");
            }
        }

        /// <summary>
        /// 根据 WL 标签频率查找具有相似拓扑特征的 face 及其标注
        /// </summary>
        /// <param name="wlFrequencies">待查询零件的 WL 标签频率列表</param>
        /// <param name="topK">返回最相似的 K 个结果</param>
        /// <param name="minSimilarity">最小相似度阈值</param>
        /// <returns>face 及其标注、相似度的列表</returns>
        public List<(int PartId, string PartName, int BodyId, string BodyName, int FaceId, int FaceIndex,
                     string LabelCategory, string LabelValue, 
                     double Similarity, double Confidence, string Notes)> 
            FindFacesByWLTags(
                List<Dictionary<string, int>> wlFrequencies, 
                int topK = 10, 
                double minSimilarity = 0.3)
        {
            var results = new List<(int, string, int, string, int, int, string, string, double, double, string)>();
            
            if (wlFrequencies.Count == 0)
            {
                Console.WriteLine("警告：WL 频率数据为空");
                return results;
            }

            // 使用最后一轮迭代进行比较
            int queryIteration = wlFrequencies.Count - 1;
            
            Console.WriteLine($"\n[调试] 查询参数：minSimilarity={minSimilarity}, topK={topK}, iteration={queryIteration}");
            Console.WriteLine($"[调试] 查询的 WL 频率：{JsonConvert.SerializeObject(wlFrequencies[queryIteration])}");

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                // 获取所有已标注 face 及其 WL 结果（使用相同的迭代轮次）
                string query = @"
                    SELECT p.id, p.part_name, b.id, b.body_name, f.id, f.face_index, 
                           ful.label_category, ful.label_value, ful.confidence, ful.notes, 
                           fwlr.label_frequencies
                    FROM parts p
                    INNER JOIN bodies b ON p.id = b.part_id
                    INNER JOIN faces f ON b.id = f.body_id
                    INNER JOIN face_wl_results fwlr ON f.id = fwlr.face_id
                    INNER JOIN face_user_labels ful ON f.id = ful.face_id
                    WHERE fwlr.iteration = @iteration
                    ORDER BY p.part_name, b.body_name, f.face_index";

                var faceData = new Dictionary<(string, string, int), (BodyWLData data, int partId, int bodyId, int faceIndex)>();

                using (var cmd = new SQLiteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@iteration", queryIteration);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int partId = reader.GetInt32(0);
                            string partName = reader.GetString(1);
                            int bodyId = reader.GetInt32(2);
                            string bodyName = reader.GetString(3);
                            int dbFaceId = reader.GetInt32(4);
                            int faceIndex = reader.GetInt32(5);
                            string category = reader.GetString(6);
                            string value = reader.GetString(7);
                            double confidence = reader.GetDouble(8);
                            string notes = reader.IsDBNull(9) ? "" : reader.GetString(9);
                            string freqJson = reader.GetString(10);

                            var frequencies = JsonConvert.DeserializeObject<Dictionary<string, int>>(freqJson);
                            
                            if (frequencies == null)
                            {
                                Console.WriteLine($"警告：无法解析 face {partName}/{bodyName}/Face{faceIndex} 的 WL 频率数据");
                                continue;
                            }

                            var key = (partName, bodyName, faceIndex);
                            if (!faceData.ContainsKey(key))
                            {
                                faceData[key] = (new BodyWLData(), partId, bodyId, faceIndex);
                            }

                            // 只取最后一轮迭代
                            if (faceData[key].data.WLFreqs.Count == 0)
                            {
                                faceData[key].data.WLFreqs.Add(frequencies);
                            }

                            faceData[key].data.Labels.Add(new LabelData(category, value, confidence, notes));
                        }
                    }
                }

                // 计算与每个 face 的相似度
                Console.WriteLine($"\n[调试] 从数据库加载了 {faceData.Count} 个已标注 face");
                
                foreach (var kvp in faceData)
                {
                    string partName = kvp.Key.Item1;
                    string bodyName = kvp.Key.Item2;
                    int faceIndex = kvp.Key.Item3;
                    var (data, partId, bodyId, _) = kvp.Value;
                    
                    double similarity = WLGraphKernel.CalculateSimilarity(wlFrequencies[queryIteration], data.WLFreqs[0]);
                    
                    // 获取第一个标注的 label_value 用于显示
                    string labelValue = data.Labels.Count > 0 ? data.Labels[0].Value : "无标注";
                    Console.WriteLine($"[调试] {partName}+{bodyName}+Face{faceIndex}: 相似度={similarity:F3}, labelValue={labelValue} (阈值={minSimilarity})");
                    
                    if (similarity >= minSimilarity)
                    {
                        // 获取 face ID
                        int dbFaceId = GetFaceId(bodyId, faceIndex);
                        
                        foreach (var label in data.Labels)
                        {
                            results.Add((partId, partName, bodyId, bodyName, dbFaceId, faceIndex, label.Category, label.Value, similarity, label.Confidence, label.Notes));
                        }
                    }
                }

                // 按相似度降序排序并取 Top-K
                results = results
                    .OrderByDescending(r => r.Item9)  // Similarity
                    .ThenByDescending(r => r.Item10)  // Confidence
                    .Take(topK)
                    .ToList();
            }

            return results;
        }

        /// <summary>
        /// 根据 body ID 和 face index 获取 face ID
        /// </summary>
        private int GetFaceId(int bodyId, int faceIndex)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                string query = "SELECT id FROM faces WHERE body_id = @body_id AND face_index = @face_index";
                using (var cmd = new SQLiteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@body_id", bodyId);
                    cmd.Parameters.AddWithValue("@face_index", faceIndex);
                    var result = cmd.ExecuteScalar();
                    return result != null ? Convert.ToInt32(result) : -1;
                }
            }
        }

        /// <summary>
        /// 添加用户标注（body 级别）
        /// 注意：同一个 label_category 只能对应一个 label_value，新的标注会覆盖旧的标注
        /// </summary>
        public void AddLabel(int bodyId, string category, string value, double confidence = 1.0, string? notes = null)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                // 先删除该 category 的旧标注（实现覆盖逻辑）
                string deleteOld = "DELETE FROM user_labels WHERE body_id = @body_id AND label_category = @category";
                using (var cmd = new SQLiteCommand(deleteOld, connection))
                {
                    cmd.Parameters.AddWithValue("@body_id", bodyId);
                    cmd.Parameters.AddWithValue("@category", category);
                    cmd.ExecuteNonQuery();
                }
                
                // 插入新标注
                string insert = @"
                    INSERT INTO user_labels (body_id, label_category, label_value, confidence, notes)
                    VALUES (@body_id, @category, @value, @confidence, @notes)";

                using (var cmd = new SQLiteCommand(insert, connection))
                {
                    cmd.Parameters.AddWithValue("@body_id", bodyId);
                    cmd.Parameters.AddWithValue("@category", category);
                    cmd.Parameters.AddWithValue("@value", value);
                    cmd.Parameters.AddWithValue("@confidence", confidence);
                    cmd.Parameters.AddWithValue("@notes", notes ?? (object)DBNull.Value);
                    cmd.ExecuteNonQuery();
                }

                Console.WriteLine($"✓ 已标注：{category} = {value} (置信度：{confidence})");
            }
        }

        /// <summary>
        /// 删除指定 ID 的标注
        /// </summary>
        public void DeleteLabel(int labelId)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                // 启用外键约束
                using (var cmd = new SQLiteCommand("PRAGMA foreign_keys = ON;", connection))
                {
                    cmd.ExecuteNonQuery();
                }
                
                string delete = "DELETE FROM user_labels WHERE id = @id";
                
                using (var cmd = new SQLiteCommand(delete, connection))
                {
                    cmd.Parameters.AddWithValue("@id", labelId);
                    int rowsAffected = cmd.ExecuteNonQuery();
                    
                    if (rowsAffected > 0)
                    {
                        Console.WriteLine($"✓ 标注 ID {labelId} 已删除");
                    }
                    else
                    {
                        Console.WriteLine($"× 未找到标注 ID {labelId}");
                    }
                }
            }
        }

        /// <summary>
        /// 删除零件 ID 为 x 的所有数据（包括 WL 结果和标注）
        /// </summary>
        public void DeletePartData(int partId)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                // 启用外键约束
                using (var cmd = new SQLiteCommand("PRAGMA foreign_keys = ON;", connection))
                {
                    cmd.ExecuteNonQuery();
                }
                
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // 直接删除零件，外键级联会自动删除 wl_results 和 user_labels 中的关联数据
                        string deletePart = "DELETE FROM parts WHERE id = @id";
                        using (var cmd = new SQLiteCommand(deletePart, connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@id", partId);
                            int rowsAffected = cmd.ExecuteNonQuery();
                            
                            if (rowsAffected > 0)
                            {
                                transaction.Commit();
                                Console.WriteLine($"✓ 零件 ID {partId} 及其所有关联数据已删除");
                            }
                            else
                            {
                                Console.WriteLine($"× 未找到零件 ID {partId}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        Console.WriteLine($"× 删除零件失败：{ex.Message}");
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// 获取 body 的所有标注
        /// </summary>
        public Dictionary<string, List<(string Value, double Confidence, string Notes)>> GetBodyLabels(int bodyId)
        {
            var result = new Dictionary<string, List<(string, double, string)>>();

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                string select = @"
                    SELECT label_category, label_value, confidence, notes 
                    FROM user_labels 
                    WHERE body_id = @body_id
                    ORDER BY created_at DESC";

                using (var cmd = new SQLiteCommand(select, connection))
                {
                    cmd.Parameters.AddWithValue("@body_id", bodyId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string category = reader.GetString(0);
                            string value = reader.GetString(1);
                            double confidence = reader.GetDouble(2);
                            string notes = reader.IsDBNull(3) ? "" : reader.GetString(3);

                            if (!result.ContainsKey(category))
                                result[category] = new List<(string, double, string)>();

                            result[category].Add((value, confidence, notes));
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 获取所有 body 及其最新标注（按零件分组显示）
        /// </summary>
        public List<(int PartId, string PartName, int BodyId, string BodyName, Dictionary<string, string> Labels)> GetAllBodiesWithLabels()
        {
            var result = new List<(int, string, int, string, Dictionary<string, string>)>();

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                // 获取所有零件
                string selectParts = "SELECT id, part_name FROM parts ORDER BY part_name";
                using (var cmd = new SQLiteCommand(selectParts, connection))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int partId = reader.GetInt32(0);
                        string partName = reader.GetString(1);

                        // 获取该零件的所有 body
                        string selectBodies = "SELECT id, body_name FROM bodies WHERE part_id = @part_id ORDER BY body_name";
                        using (var cmd2 = new SQLiteCommand(selectBodies, connection))
                        {
                            cmd2.Parameters.AddWithValue("@part_id", partId);
                            using (var reader2 = cmd2.ExecuteReader())
                            {
                                while (reader2.Read())
                                {
                                    int bodyId = reader2.GetInt32(0);
                                    string bodyName = reader2.GetString(1);

                                    // 获取该 body 的最新标注
                                    var labels = GetLatestLabels(connection, bodyId);
                                    
                                    result.Add((partId, partName, bodyId, bodyName, labels));
                                }
                            }
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 获取 body 的最新标注（每个类别取最新的）
        /// </summary>
        private Dictionary<string, string> GetLatestLabels(SQLiteConnection connection, int bodyId)
        {
            var labels = new Dictionary<string, string>();

            string select = @"
                SELECT label_category, label_value 
                FROM (
                    SELECT label_category, label_value, 
                           ROW_NUMBER() OVER (PARTITION BY label_category ORDER BY created_at DESC) as rn
                    FROM user_labels
                    WHERE body_id = @body_id
                )
                WHERE rn = 1";

            using (var cmd = new SQLiteCommand(select, connection))
            {
                cmd.Parameters.AddWithValue("@body_id", bodyId);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string category = reader.GetString(0);
                        string value = reader.GetString(1);
                        labels[category] = value;
                    }
                }
            }

            return labels;
        }

        /// <summary>
        /// 根据标注类别查询 body
        /// </summary>
        public List<(int PartId, string PartName, int BodyId, string BodyName, string LabelValue)> SearchByLabel(string category, string? value = null)
        {
            var result = new List<(int, string, int, string, string)>();

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                string query = @"
                    SELECT p.id, p.part_name, b.id, b.body_name, ul.label_value
                    FROM parts p
                    INNER JOIN bodies b ON p.id = b.part_id
                    INNER JOIN user_labels ul ON b.id = ul.body_id
                    WHERE ul.label_category = @category";

                if (!string.IsNullOrEmpty(value))
                {
                    query += " AND ul.label_value = @value";
                }

                query += " ORDER BY p.part_name, b.body_name";

                using (var cmd = new SQLiteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@category", category);
                    if (!string.IsNullOrEmpty(value))
                    {
                        cmd.Parameters.AddWithValue("@value", value);
                    }

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.Add((
                                reader.GetInt32(0),
                                reader.GetString(1),
                                reader.GetInt32(2),
                                reader.GetString(3),
                                reader.GetString(4)
                            ));
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 导出 body 的 WL 结果为 JSON
        /// </summary>
        public string ExportWLResult(int bodyId)
        {
            var result = new
            {
                BodyId = bodyId,
                Iterations = new List<object>()
            };

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                string select = "SELECT iteration, label_frequencies FROM wl_results WHERE body_id = @body_id ORDER BY iteration";
                
                using (var cmd = new SQLiteCommand(select, connection))
                {
                    cmd.Parameters.AddWithValue("@body_id", bodyId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.Iterations.Add(new
                            {
                                Iteration = reader.GetInt32(0),
                                Frequencies = JsonConvert.DeserializeObject<Dictionary<string, int>>(reader.GetString(1))
                            });
                        }
                    }
                }
            }

            return JsonConvert.SerializeObject(result, Formatting.Indented);
        }

        /// <summary>
        /// 根据零件名获取所有标签并返回 join 后的字符串
        /// </summary>
        /// <param name="partName">零件名（不带后缀）</param>
        /// <returns>所有标签的 join 字符串，格式：bodyName1,label1;bodyName2,label2</returns>
        public string GetLabelsByPartName(string partName)
        {
            // 去掉文件名后缀
            string partNameWithoutExtension = partName;
                    
            var result = new System.Text.StringBuilder();
                    
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                        
                // 查找零件
                string selectPart = "SELECT id FROM parts WHERE part_name = @part_name";
                using (var cmd = new SQLiteCommand(selectPart, connection))
                {
                    cmd.Parameters.AddWithValue("@part_name", partNameWithoutExtension);
                    var partIdObj = cmd.ExecuteScalar();
                    if (partIdObj == null)
                    {
                        return string.Empty; // 零件不存在
                    }
                            
                    int partId = Convert.ToInt32(partIdObj);
                            
                    // 获取该零件的所有 body
                    string selectBodies = "SELECT id, body_name FROM bodies WHERE part_id = @part_id ORDER BY body_name";
                      
                    using (var cmd2 = new SQLiteCommand(selectBodies, connection))
                    {
                        cmd2.Parameters.AddWithValue("@part_id", partId);
                                  
                        using (var reader = cmd2.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int bodyId = reader.GetInt32(0);
                                string bodyName = reader.GetString(1);
                                        
                                // 获取该 body 的所有最新标注
                                var labels = GetLatestLabels(connection, bodyId);
                                    
                                if (labels.Count > 0)
                                {
                                    foreach (var label in labels.Values)
                                    {
                                        if (result.Length > 0)
                                        {
                                            result.Append(";");
                                        }
                                                
                                        // 格式：bodyName,label
                                        result.Append($"{bodyName},{label}");
                                    }
                                }
                            }
                        }
                    }
                }
            }
                    
            return result.ToString();
        }

        /// <summary>
        /// 统计数据库信息
        /// </summary>
        public (int PartCount, int BodyCount, int LabelCount, Dictionary<string, int> CategoryStats) GetStatistics()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                // 统计零件数
                int partCount = 0;
                using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM parts", connection))
                {
                    partCount = Convert.ToInt32(cmd.ExecuteScalar());
                }

                // 统计 body 数
                int bodyCount = 0;
                using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM bodies", connection))
                {
                    bodyCount = Convert.ToInt32(cmd.ExecuteScalar());
                }

                // 统计标注数
                int labelCount = 0;
                using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM user_labels", connection))
                {
                    labelCount = Convert.ToInt32(cmd.ExecuteScalar());
                }

                // 按类别统计
                var categoryStats = new Dictionary<string, int>();
                using (var cmd = new SQLiteCommand(
                    "SELECT label_category, COUNT(*) FROM user_labels GROUP BY label_category", 
                    connection))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        categoryStats[reader.GetString(0)] = reader.GetInt32(1);
                    }
                }

                return (partCount, bodyCount, labelCount, categoryStats);
            }
        }

        /// <summary>
        /// 获取所有已使用的标注类别
        /// </summary>
        public List<string> GetAllCategories()
        {
            var categories = new List<string>();

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                string query = "SELECT DISTINCT label_category FROM user_labels ORDER BY label_category";

                using (var cmd = new SQLiteCommand(query, connection))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        categories.Add(reader.GetString(0));
                    }
                }
            }

            return categories;
        }

        /// <summary>
        /// 获取所有零件
        /// </summary>
        public List<(int PartId, string Name, string Path)> GetAllParts()
        {
            var parts = new List<(int, string, string)>();

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                string query = "SELECT id, part_name, file_path FROM parts ORDER BY part_name";

                using (var cmd = new SQLiteCommand(query, connection))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int partId = reader.GetInt32(0);
                        string partName = reader.GetString(1);
                        string filePath = reader.GetString(2);
                        parts.Add((partId, partName, filePath));
                    }
                }
            }

            return parts;
        }

        /// <summary>
        /// 清空所有 WL 结果数据（用于重置）
        /// </summary>
        public void ClearAllWLResults()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                using (var cmd = new SQLiteCommand("DELETE FROM wl_results", connection))
                {
                    int rowsAffected = cmd.ExecuteNonQuery();
                    Console.WriteLine($"✓ 已删除 {rowsAffected} 条 WL 结果记录");
                }
            }
        }

        /// <summary>
        /// 清空所有标注数据
        /// </summary>
        public void ClearAllLabels()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                using (var cmd = new SQLiteCommand("DELETE FROM user_labels", connection))
                {
                    int rowsAffected = cmd.ExecuteNonQuery();
                    Console.WriteLine($"✓ 已删除 {rowsAffected} 条标注记录");
                }
            }
        }

        /// <summary>
        /// 清空所有数据（包括零件、body、WL 结果和标注）
        /// </summary>
        public void ClearAll()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                // 启用外键约束
                using (var cmd = new SQLiteCommand("PRAGMA foreign_keys = ON;", connection))
                {
                    cmd.ExecuteNonQuery();
                }
                
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // 删除所有标注
                        using (var cmd = new SQLiteCommand("DELETE FROM user_labels", connection, transaction))
                        {
                            int labelsDeleted = cmd.ExecuteNonQuery();
                            Console.WriteLine($"✓ 已删除 {labelsDeleted} 条标注记录");
                        }
                        
                        // 删除所有 WL 结果
                        using (var cmd = new SQLiteCommand("DELETE FROM wl_results", connection, transaction))
                        {
                            int wlDeleted = cmd.ExecuteNonQuery();
                            Console.WriteLine($"✓ 已删除 {wlDeleted} 条 WL 结果记录");
                        }
                        
                        // 删除所有 body（级联删除会自动处理）
                        using (var cmd = new SQLiteCommand("DELETE FROM bodies", connection, transaction))
                        {
                            int bodiesDeleted = cmd.ExecuteNonQuery();
                            Console.WriteLine($"✓ 已删除 {bodiesDeleted} 个 body 记录");
                        }
                        
                        // 删除所有零件
                        using (var cmd = new SQLiteCommand("DELETE FROM parts", connection, transaction))
                        {
                            int partsDeleted = cmd.ExecuteNonQuery();
                            Console.WriteLine($"✓ 已删除 {partsDeleted} 个零件记录");
                        }
                        
                        transaction.Commit();
                        Console.WriteLine("✓ 数据库已完全清空");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        Console.WriteLine($"× 清空数据库失败：{ex.Message}");
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// 插入零件记录（不带 body）
        /// </summary>
        public int InsertPart(string partName, string filePath)
        {
            // 去掉文件名后缀
            string partNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(partName);
            
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                // 先检查是否已存在
                string select = "SELECT id FROM parts WHERE part_name = @part_name AND file_path = @file_path";
                using (var cmd = new SQLiteCommand(select, connection))
                {
                    cmd.Parameters.AddWithValue("@part_name", partNameWithoutExtension);
                    cmd.Parameters.AddWithValue("@file_path", filePath);
                    var result = cmd.ExecuteScalar();
                    if (result != null)
                    {
                        return Convert.ToInt32(result);
                    }
                }

                // 不存在则插入
                string insert = @"
                    INSERT INTO parts (part_name, file_path) 
                    VALUES (@part_name, @file_path)";
                
                using (var cmd = new SQLiteCommand(insert, connection))
                {
                    cmd.Parameters.AddWithValue("@part_name", partNameWithoutExtension);
                    cmd.Parameters.AddWithValue("@file_path", filePath);
                    cmd.ExecuteNonQuery();
                    
                    // 返回新插入的 ID
                    cmd.CommandText = "SELECT last_insert_rowid()";
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
        }

        /// <summary>
        /// 根据 WL 标签频率查找具有相似拓扑特征的 body 类别（使用指定迭代轮次）
        /// </summary>
        /// <param name="wlFrequencies">待查询零件的 WL 标签频率列表</param>
        /// <param name="topK">返回最相似的 K 个类别</param>
        /// <param name="minSimilarity">最小相似度阈值</param>
        /// <returns>类别及其相似度、置信度的列表</returns>
        public List<(string Category, string PartName, string BodyName, double Similarity, double Confidence, string Notes)> 
            FindCategoriesByWLTags(
                List<Dictionary<string, int>> wlFrequencies, 
                int topK = 10, 
                double minSimilarity = 0.3)
        {
            var results = new List<(string, string, string, double, double, string)>();
            
            if (wlFrequencies.Count == 0)
            {
                Console.WriteLine("警告：WL 频率数据为空");
                return results;
            }

            // 使用最后一轮迭代进行比较
            int queryIteration = wlFrequencies.Count - 1;
            
            Console.WriteLine($"\n[调试] 查询参数：minSimilarity={minSimilarity}, topK={topK}, iteration={queryIteration}");
            Console.WriteLine($"[调试] 查询的 WL 频率：{JsonConvert.SerializeObject(wlFrequencies[queryIteration])}");

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                // 获取所有已标注 body 及其 WL 结果（使用相同的迭代轮次）
                string query = @"
                    SELECT p.id, p.part_name, b.id, b.body_name, ul.label_category, ul.label_value, 
                           ul.confidence, ul.notes, wr.label_frequencies
                    FROM parts p
                    INNER JOIN bodies b ON p.id = b.part_id
                    INNER JOIN wl_results wr ON b.id = wr.body_id
                    INNER JOIN user_labels ul ON b.id = ul.body_id
                    WHERE wr.iteration = @iteration
                    ORDER BY p.part_name, b.body_name";

                var bodyData = new Dictionary<(string, string), BodyWLData>();

                using (var cmd = new SQLiteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@iteration", queryIteration);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string partName = reader.GetString(1);
                            string bodyName = reader.GetString(3);
                            string category = reader.GetString(4);
                            string value = reader.GetString(5);
                            double confidence = reader.GetDouble(6);
                            string notes = reader.IsDBNull(7) ? "" : reader.GetString(7);
                            string freqJson = reader.GetString(8);

                            var frequencies = JsonConvert.DeserializeObject<Dictionary<string, int>>(freqJson);
                            
                            if (frequencies == null)
                            {
                                Console.WriteLine($"警告：无法解析 body {partName}/{bodyName} 的 WL 频率数据");
                                continue;
                            }

                            var key = (partName, bodyName);
                            if (!bodyData.ContainsKey(key))
                            {
                                bodyData[key] = new BodyWLData();
                            }

                            bodyData[key].WLFreqs.Add(frequencies);
                            bodyData[key].Labels.Add(new LabelData(category, value, confidence, notes));
                        }
                    }
                }

                // 计算与每个 body 的相似度
                Console.WriteLine($"\n[调试] 从数据库加载了 {bodyData.Count} 个已标注 body");
                
                foreach (var kvp in bodyData)
                {
                    string partName = kvp.Key.Item1;
                    string bodyName = kvp.Key.Item2;
                    BodyWLData data = kvp.Value;
                    
                    double similarity = WLGraphKernel.CalculateSimilarity(wlFrequencies[queryIteration], data.WLFreqs[0]);
                    
                    Console.WriteLine($"[调试] {bodyName}: 相似度={similarity:F3} (阈值={minSimilarity})");
                    
                    if (similarity >= minSimilarity)
                    {
                        foreach (var label in data.Labels)
                        {
                            // 返回 label_value 而不是 label_category
                            results.Add((label.Value, partName, bodyName, similarity, label.Confidence, label.Notes));
                        }
                    }
                }

                // 按相似度降序排序并取 Top-K
                results = results
                    .OrderByDescending(r => r.Item4)  // Similarity
                    .ThenByDescending(r => r.Item5)  // Confidence
                    .Take(topK)
                    .ToList();
            }

            return results;
        }

        /// <summary>
        /// 根据 WL 标签查找类别（简化版，返回推荐类别及出现频率）
        /// </summary>
        /// <param name="wlFrequencies">待查询零件的 WL 标签频率</param>
        /// <param name="topK">返回最相关的 K 个类别</param>
        /// <param name="minSimilarity">最小相似度阈值</param>
        /// <returns>类别名称、推荐次数、平均相似度、平均置信度</returns>
        public List<(string Category, int Count, double AvgSimilarity, double AvgConfidence)> 
            FindTopCategoriesByWLTags(
                List<Dictionary<string, int>> wlFrequencies, 
                int topK = 5, 
                double minSimilarity = 0.3)
        {
            var detailedResults = FindCategoriesByWLTags(wlFrequencies, topK: 100, minSimilarity: minSimilarity);
            
                // 按 body 去重后，再按 label_value 分组统计
                var bodyValues = detailedResults
                    .GroupBy(r => r.PartName + "|" + r.BodyName)  // 先按 body 唯一标识分组
                    .Select(g => 
                    {
                        // 对于每个 body，取相似度最高的那个标注值
                        var bestMatch = g.OrderByDescending(r => r.Similarity).First();
                        return new
                        {
                            BodyKey = g.Key,
                            LabelValue = bestMatch.Category,  // Category 字段实际存储的是 label_value
                            Similarity = bestMatch.Similarity,
                            Confidence = bestMatch.Confidence
                        };
                    })
                    .ToList();
                
                // 按标注值分组统计
                var valueGroups = bodyValues
                    .GroupBy(x => x.LabelValue)
                    .Select(g => new
                    {
                        Value = g.Key,
                        Count = g.Count(),
                        AvgSimilarity = g.Average(x => x.Similarity),
                        AvgConfidence = g.Average(x => x.Confidence)
                    })
                    .OrderByDescending(x => x.Count)
                    .ThenByDescending(x => x.AvgSimilarity)
                    .Take(topK)
                    .ToList();

                return valueGroups
                    .Select(x => (x.Value, x.Count, x.AvgSimilarity, x.AvgConfidence))
                    .ToList();
        }

        /// <summary>
        /// 根据 WL 标签频率查找具有相似拓扑特征的 body 及其标注（直接返回标注信息）
        /// </summary>
        /// <param name="wlFrequencies">待查询零件的 WL 标签频率列表</param>
        /// <param name="topK">返回最相似的 K 个结果</param>
        /// <param name="minSimilarity">最小相似度阈值</param>
        /// <returns>body 及其标注、相似度的列表</returns>
        public List<(int PartId, string PartName, int BodyId, string BodyName, 
                     string LabelCategory, string LabelValue, 
                     double Similarity, double Confidence, string Notes)> 
            FindBodiesByWLTags(
                List<Dictionary<string, int>> wlFrequencies, 
                int topK = 10, 
                double minSimilarity = 0.3,
                string? labelValueKeyword = null)
        {
            var results = new List<(int, string, int, string, string, string, double, double, string)>();
            var totalSw = System.Diagnostics.Stopwatch.StartNew();
            
            if (wlFrequencies.Count == 0)
            {
                Console.WriteLine("警告：WL 频率数据为空");
                return results;
            }

            // 使用最后一轮迭代进行比较
            int queryIteration = wlFrequencies.Count - 1;
            
            Console.WriteLine($"\n[调试] 查询参数：minSimilarity={minSimilarity}, topK={topK}, iteration={queryIteration}");
            Console.WriteLine($"[调试] 查询的 WL 频率：{JsonConvert.SerializeObject(wlFrequencies[queryIteration])}");

            int dbRowCount = 0;
            var loadSw = System.Diagnostics.Stopwatch.StartNew();
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                // 获取所有已标注 body 及其 WL 结果（使用相同的迭代轮次）
                string query = @"
                    SELECT p.id, p.part_name, b.id, b.body_name, ul.label_category, ul.label_value, 
                           ul.confidence, ul.notes, wr.label_frequencies
                    FROM parts p
                    INNER JOIN bodies b ON p.id = b.part_id
                    INNER JOIN wl_results wr ON b.id = wr.body_id
                    INNER JOIN user_labels ul ON b.id = ul.body_id
                    WHERE wr.iteration = @iteration";

                if (!string.IsNullOrWhiteSpace(labelValueKeyword))
                {
                    query += " AND ul.label_value LIKE @label_value_keyword";
                }

                query += @"
                    ORDER BY p.part_name, b.body_name";

                var bodyData = new Dictionary<(int PartId, int BodyId, string PartName, string BodyName), BodyWLData>();

                using (var cmd = new SQLiteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@iteration", queryIteration);
                    if (!string.IsNullOrWhiteSpace(labelValueKeyword))
                    {
                        cmd.Parameters.AddWithValue("@label_value_keyword", $"%{labelValueKeyword.Trim()}%");
                    }
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int partId = reader.GetInt32(0);
                            string partName = reader.GetString(1);
                            int bodyId = reader.GetInt32(2);
                            string bodyName = reader.GetString(3);
                            string category = reader.GetString(4);
                            string value = reader.GetString(5);
                            double confidence = reader.GetDouble(6);
                            string notes = reader.IsDBNull(7) ? "" : reader.GetString(7);
                            string freqJson = reader.GetString(8);
                            dbRowCount++;

                            var key = (partId, bodyId, partName, bodyName);
                            if (!bodyData.ContainsKey(key))
                            {
                                bodyData[key] = new BodyWLData();
                            }

                            // 只取最后一轮迭代
                            if (bodyData[key].WLFreqs.Count == 0)
                            {
                                bodyData[key].WLFreqs.Add(JsonConvert.DeserializeObject<Dictionary<string, int>>(freqJson)!);
                            }

                            bodyData[key].Labels.Add(new LabelData(category, value, confidence, notes));
                        }
                    }
                }
                loadSw.Stop();

                // 计算与每个 body 的相似度
                Console.WriteLine($"\n[调试] 从数据库加载了 {bodyData.Count} 个已标注 body");
                var similaritySw = System.Diagnostics.Stopwatch.StartNew();
                
                foreach (var kvp in bodyData)
                {
                    int partId = kvp.Key.PartId;
                    int bodyId = kvp.Key.BodyId;
                    string partName = kvp.Key.PartName;
                    string bodyName = kvp.Key.BodyName;
                    BodyWLData data = kvp.Value;
                    
                    double similarity = WLGraphKernel.CalculateSimilarity(wlFrequencies[queryIteration], data.WLFreqs[0]);
                    
                    // 获取第一个标注的 label_value 用于显示
                    string labelValue = data.Labels.Count > 0 ? data.Labels[0].Value : "无标注";
                    Console.WriteLine($"[调试] {partName}+{bodyName}: 相似度={similarity:F3}, labelValue={labelValue} (阈值={minSimilarity})");
                    
                    if (similarity >= minSimilarity)
                    {
                        foreach (var label in data.Labels)
                        {
                            results.Add((partId, partName, bodyId, bodyName, label.Category, label.Value, similarity, label.Confidence, label.Notes));
                        }
                    }
                }
                similaritySw.Stop();

                // 按相似度降序排序并取 Top-K
                results = results
                    .OrderByDescending(r => r.Item7)  // Similarity
                    .ThenByDescending(r => r.Item8)  // Confidence
                    .Take(topK)
                    .ToList();

                totalSw.Stop();
                Console.WriteLine($"[性能] WL查询耗时: 总计={totalSw.ElapsedMilliseconds}ms, 载入={loadSw.ElapsedMilliseconds}ms, 相似度计算={similaritySw.ElapsedMilliseconds}ms, SQL行数={dbRowCount}, body候选={bodyData.Count}, 命中标签={results.Count}");
            }

            return results;
        }

        /// <summary>
        /// 根据零件名获取零件 ID
        /// </summary>
        private int GetPartIdByName(string partName)
        {
            // 去掉文件名后缀
            string partNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(partName);
                    
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                string query = "SELECT id FROM parts WHERE part_name = @part_name";
                using (var cmd = new SQLiteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@part_name", partNameWithoutExtension);
                    var result = cmd.ExecuteScalar();
                    return result != null ? Convert.ToInt32(result) : -1;
                }
            }
        }

        /// <summary>
        /// 根据零件 ID 和 body 名称获取 body ID
        /// </summary>
        private int GetBodyId(int partId, string bodyName)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                string query = "SELECT id FROM bodies WHERE part_id = @part_id AND body_name = @body_name";
                using (var cmd = new SQLiteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@part_id", partId);
                    cmd.Parameters.AddWithValue("@body_name", bodyName);
                    var result = cmd.ExecuteScalar();
                    return result != null ? Convert.ToInt32(result) : -1;
                }
            }
        }
    }
}
