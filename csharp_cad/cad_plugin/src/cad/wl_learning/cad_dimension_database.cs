using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using Newtonsoft.Json;

namespace cad_tools
{
    /// <summary>
    /// CAD标注数据库 - 共享topology_labels.db，但使用独立表
    /// </summary>
    public class CADDimensionDatabase
    {
        private readonly string _connectionString;
        private readonly string _dbPath;

        public CADDimensionDatabase(string dbPath = "topology_labels.db")
        {
            _dbPath = dbPath;
            _connectionString = $"Data Source={dbPath};Version=3;";
            InitializeDatabase();
        }

        /// <summary>
        /// 初始化CAD标注数据库表结构
        /// </summary>
        private void InitializeDatabase()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                using (var cmd = new SQLiteCommand("PRAGMA foreign_keys = ON;", connection))
                {
                    cmd.ExecuteNonQuery();
                }

                // CAD图形表
                string createCADGraphsTable = @"
                    CREATE TABLE IF NOT EXISTS cad_graphs (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        graph_name TEXT NOT NULL,
                        file_path TEXT NOT NULL,
                        view_name TEXT,
                        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                        UNIQUE(graph_name, file_path, view_name)
                    );";

                // CAD图节点表
                string createCADNodesTable = @"
                    CREATE TABLE IF NOT EXISTS cad_nodes (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        graph_id INTEGER NOT NULL,
                        node_id INTEGER NOT NULL,
                        edge_type TEXT NOT NULL,
                        geometry_value REAL,
                        angle REAL,
                        is_horizontal INTEGER DEFAULT 0,
                        is_vertical INTEGER DEFAULT 0,
                        FOREIGN KEY (graph_id) REFERENCES cad_graphs(id) ON DELETE CASCADE,
                        UNIQUE(graph_id, node_id)
                    );";

                // CAD图连接关系表
                string createCADEdgesTable = @"
                    CREATE TABLE IF NOT EXISTS cad_edges (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        graph_id INTEGER NOT NULL,
                        from_node INTEGER NOT NULL,
                        to_node INTEGER NOT NULL,
                        FOREIGN KEY (graph_id) REFERENCES cad_graphs(id) ON DELETE CASCADE
                    );";

                // CAD WL结果表
                string createCADWLResultsTable = @"
                    CREATE TABLE IF NOT EXISTS cad_wl_results (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        graph_id INTEGER NOT NULL,
                        iteration INTEGER NOT NULL,
                        label_frequencies TEXT NOT NULL,
                        FOREIGN KEY (graph_id) REFERENCES cad_graphs(id) ON DELETE CASCADE,
                        UNIQUE(graph_id, iteration)
                    );";

                // CAD标注规则表（存储学习到的标注规则）
                string createCADRulesTable = @"
                    CREATE TABLE IF NOT EXISTS cad_dimension_rules (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        graph_id INTEGER NOT NULL,
                        rule_name TEXT NOT NULL,
                        rule_type TEXT NOT NULL,
                        rule_pattern TEXT,
                        dimension_value TEXT,
                        dimension_type TEXT,
                        reference_nodes TEXT,
                        annotation_style TEXT,
                        confidence REAL DEFAULT 1.0,
                        notes TEXT,
                        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (graph_id) REFERENCES cad_graphs(id) ON DELETE CASCADE
                    );";

                // 创建索引
                string createIndexes = @"
                    CREATE INDEX IF NOT EXISTS idx_cad_graph_name ON cad_graphs(graph_name);
                    CREATE INDEX IF NOT EXISTS idx_cad_wl_graph ON cad_wl_results(graph_id);
                    CREATE INDEX IF NOT EXISTS idx_cad_rule_type ON cad_dimension_rules(rule_type);
                ";

                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = createCADGraphsTable;
                    command.ExecuteNonQuery();

                    command.CommandText = createCADNodesTable;
                    command.ExecuteNonQuery();

                    command.CommandText = createCADEdgesTable;
                    command.ExecuteNonQuery();

                    command.CommandText = createCADWLResultsTable;
                    command.ExecuteNonQuery();

                    command.CommandText = createCADRulesTable;
                    command.ExecuteNonQuery();

                    command.CommandText = createIndexes;
                    command.ExecuteNonQuery();
                }

                Console.WriteLine($"CAD标注数据库初始化完成：{_dbPath}");
            }
        }

        /// <summary>
        /// 保存CAD图形及其WL结果
        /// </summary>
        public int UpsertCADGraphWithWL(CADGraphEdgeGraph graph, List<Dictionary<string, int>> wlFrequencies)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // 插入或更新图形信息
                        int graphId = GetOrCreateCADGraph(connection, graph.GraphName, graph.SourceFile, "");
                        
                        // 保存节点信息
                        SaveNodes(connection, graphId, graph.Nodes, transaction);
                        
                        // 保存连接关系
                        SaveEdges(connection, graphId, graph.Nodes, transaction);
                        
                        // 删除旧的WL结果
                        string deleteOld = "DELETE FROM cad_wl_results WHERE graph_id = @graph_id";
                        using (var cmd = new SQLiteCommand(deleteOld, connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@graph_id", graphId);
                            cmd.ExecuteNonQuery();
                        }

                        // 保存WL结果
                        for (int iter = 0; iter < wlFrequencies.Count; iter++)
                        {
                            string insertWL = @"
                                INSERT INTO cad_wl_results (graph_id, iteration, label_frequencies) 
                                VALUES (@graph_id, @iteration, @frequencies)";

                            using (var cmd = new SQLiteCommand(insertWL, connection, transaction))
                            {
                                cmd.Parameters.Clear();
                                cmd.Parameters.AddWithValue("@graph_id", graphId);
                                cmd.Parameters.AddWithValue("@iteration", iter);
                                cmd.Parameters.AddWithValue("@frequencies", JsonConvert.SerializeObject(wlFrequencies[iter]));
                                cmd.ExecuteNonQuery();
                            }
                        }
                        
                        transaction.Commit();
                        Console.WriteLine($"✓ CAD图形 '{graph.GraphName}' 已存储（{graph.Nodes.Count} 个节点）");
                        
                        return graphId;
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        Console.WriteLine($"× 存储CAD图形失败：{ex.Message}");
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// 添加标注规则
        /// </summary>
        public void AddDimensionRule(int graphId, string ruleName, string ruleType, 
            string dimensionValue, string dimensionType, string annotationStyle, 
            double confidence = 1.0, string notes = "", string rulePattern = "", string referenceNodes = "")
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                string insert = @"
                    INSERT INTO cad_dimension_rules (
                        graph_id, rule_name, rule_type, dimension_value, 
                        dimension_type, annotation_style, confidence, notes, rule_pattern, reference_nodes)
                    VALUES (@graph_id, @rule_name, @rule_type, @dimension_value,
                            @dimension_type, @annotation_style, @confidence, @notes, @rule_pattern, @reference_nodes)";

                using (var cmd = new SQLiteCommand(insert, connection))
                {
                    cmd.Parameters.AddWithValue("@graph_id", graphId);
                    cmd.Parameters.AddWithValue("@rule_name", ruleName);
                    cmd.Parameters.AddWithValue("@rule_type", ruleType);
                    cmd.Parameters.AddWithValue("@dimension_value", dimensionValue);
                    cmd.Parameters.AddWithValue("@dimension_type", dimensionType);
                    cmd.Parameters.AddWithValue("@annotation_style", annotationStyle);
                    cmd.Parameters.AddWithValue("@confidence", confidence);
                    cmd.Parameters.AddWithValue("@notes", notes);
                    cmd.Parameters.AddWithValue("@rule_pattern", rulePattern);
                    cmd.Parameters.AddWithValue("@reference_nodes", referenceNodes);
                    cmd.ExecuteNonQuery();
                }

                Console.WriteLine($"✓ 已添加标注规则：{ruleName} = {dimensionValue}");
            }
        }

        /// <summary>
        /// 根据WL相似度查找推荐的标注规则
        /// </summary>
        public List<(string RuleName, string DimensionValue, string DimensionType, 
                     double Similarity, double Confidence, string AnnotationStyle, string RuleType, string SourceGraph)> 
            FindRecommendedRules(List<Dictionary<string, int>> wlFrequencies, 
                                 int topK = 10, 
                                 double minSimilarity = 0.5,
                                 int minDistinctGraphs = 1)
        {
            var results = new List<(string, string, string, double, double, string, string, string)>();
            
            if (wlFrequencies.Count == 0)
            {
                Console.WriteLine("警告：WL频率数据为空");
                return results;
            }

            int queryIteration = wlFrequencies.Count - 1;

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                
                // 获取所有已标注的CAD图形及其WL结果
                string query = @"
                    SELECT g.id, g.graph_name, dr.rule_name, dr.dimension_value, 
                           dr.dimension_type, dr.annotation_style, dr.confidence, dr.rule_type,
                           wr.label_frequencies
                    FROM cad_graphs g
                    INNER JOIN cad_wl_results wr ON g.id = wr.graph_id
                    INNER JOIN cad_dimension_rules dr ON g.id = dr.graph_id
                    WHERE wr.iteration = @iteration
                    ORDER BY g.graph_name";

                var graphData = new Dictionary<string, (CADGraphEdgeWLData data, int graphId)>();

                using (var cmd = new SQLiteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@iteration", queryIteration);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int graphId = reader.GetInt32(0);
                            string graphName = reader.GetString(1);
                            string ruleName = reader.GetString(2);
                            string dimensionValue = reader.GetString(3);
                            string dimensionType = reader.GetString(4);
                            string annotationStyle = reader.GetString(5);
                            double confidence = reader.GetDouble(6);
                            string ruleType = reader.GetString(7);
                            string freqJson = reader.GetString(8);

                            var frequencies = JsonConvert.DeserializeObject<Dictionary<string, int>>(freqJson);
                            
                            if (frequencies == null) continue;

                            if (!graphData.ContainsKey(graphName))
                            {
                                graphData[graphName] = (new CADGraphEdgeWLData(), graphId);
                            }

                            if (graphData[graphName].data.WLFreqs.Count == 0)
                            {
                                graphData[graphName].data.WLFreqs.Add(frequencies);
                            }

                            graphData[graphName].data.Rules.Add(new CADRuleData(
                                ruleName, dimensionValue, dimensionType, 
                                annotationStyle, confidence, ruleType));
                        }
                    }
                }

                if (graphData.Count < minDistinctGraphs)
                {
                    return results;
                }

                // 计算相似度
                foreach (var kvp in graphData)
                {
                    string graphName = kvp.Key;
                    var (data, graphId) = kvp.Value;
                    
                    double similarity = CADWLGraphKernel.CalculateSimilarity(
                        wlFrequencies[queryIteration], 
                        data.WLFreqs[0]);
                    
                    if (similarity >= minSimilarity)
                    {
                        foreach (var rule in data.Rules)
                        {
                            results.Add((rule.RuleName, rule.DimensionValue, 
                                       rule.DimensionType, similarity, 
                                       rule.Confidence, rule.AnnotationStyle, rule.RuleType, graphName));
                        }
                    }
                }

                // 按相似度排序
                results = results
                    .OrderByDescending(r => r.Item4)
                    .ThenByDescending(r => r.Item5)
                    .Take(topK)
                    .ToList();
            }

            return results;
        }

        public int GetAnnotatedGraphCount()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                string query = @"
                    SELECT COUNT(DISTINCT g.id)
                    FROM cad_graphs g
                    INNER JOIN cad_dimension_rules dr ON g.id = dr.graph_id";
                using (var cmd = new SQLiteCommand(query, connection))
                {
                    object? result = cmd.ExecuteScalar();
                    return result == null ? 0 : Convert.ToInt32(result);
                }
            }
        }

        /// <summary>
        /// 获取或创建CAD图形记录
        /// </summary>
        private int GetOrCreateCADGraph(SQLiteConnection connection, 
                                       string graphName, 
                                       string filePath, 
                                       string viewName)
        {
            string select = "SELECT id FROM cad_graphs WHERE graph_name = @graph_name AND file_path = @file_path AND (view_name = @view_name OR (view_name IS NULL AND @view_name = ''))";
            using (var cmd = new SQLiteCommand(select, connection))
            {
                cmd.Parameters.AddWithValue("@graph_name", graphName);
                cmd.Parameters.AddWithValue("@file_path", filePath);
                cmd.Parameters.AddWithValue("@view_name", string.IsNullOrEmpty(viewName) ? (object)DBNull.Value : viewName);
                var result = cmd.ExecuteScalar();
                if (result != null)
                {
                    return Convert.ToInt32(result);
                }
            }

            string insert = @"
                INSERT INTO cad_graphs (graph_name, file_path, view_name) 
                VALUES (@graph_name, @file_path, @view_name)";
            
            using (var cmd = new SQLiteCommand(insert, connection))
            {
                cmd.Parameters.AddWithValue("@graph_name", graphName);
                cmd.Parameters.AddWithValue("@file_path", filePath);
                cmd.Parameters.AddWithValue("@view_name", string.IsNullOrEmpty(viewName) ? (object)DBNull.Value : viewName);
                cmd.ExecuteNonQuery();
                
                cmd.CommandText = "SELECT last_insert_rowid()";
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        /// <summary>
        /// 保存节点信息
        /// </summary>
        private void SaveNodes(SQLiteConnection connection, int graphId, 
                              List<CADGraphEdgeNode> nodes, SQLiteTransaction transaction)
        {
            // 删除旧节点
            string deleteOld = "DELETE FROM cad_nodes WHERE graph_id = @graph_id";
            using (var cmd = new SQLiteCommand(deleteOld, connection, transaction))
            {
                cmd.Parameters.AddWithValue("@graph_id", graphId);
                cmd.ExecuteNonQuery();
            }

            // 插入新节点
            foreach (var node in nodes)
            {
                string insert = @"
                    INSERT INTO cad_nodes (graph_id, node_id, edge_type, geometry_value, 
                                          angle, is_horizontal, is_vertical)
                    VALUES (@graph_id, @node_id, @edge_type, @geometry_value,
                            @angle, @is_horizontal, @is_vertical)";

                using (var cmd = new SQLiteCommand(insert, connection, transaction))
                {
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("@graph_id", graphId);
                    cmd.Parameters.AddWithValue("@node_id", node.Id);
                    cmd.Parameters.AddWithValue("@edge_type", node.EdgeType);
                    cmd.Parameters.AddWithValue("@geometry_value", node.GeometryValue);
                    cmd.Parameters.AddWithValue("@angle", node.Angle);
                    cmd.Parameters.AddWithValue("@is_horizontal", node.IsHorizontal ? 1 : 0);
                    cmd.Parameters.AddWithValue("@is_vertical", node.IsVertical ? 1 : 0);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// 保存连接关系
        /// </summary>
        private void SaveEdges(SQLiteConnection connection, int graphId,
                              List<CADGraphEdgeNode> nodes, SQLiteTransaction transaction)
        {
            string deleteOld = "DELETE FROM cad_edges WHERE graph_id = @graph_id";
            using (var cmd = new SQLiteCommand(deleteOld, connection, transaction))
            {
                cmd.Parameters.AddWithValue("@graph_id", graphId);
                cmd.ExecuteNonQuery();
            }

            foreach (var node in nodes)
            {
                foreach (var connectedId in node.ConnectedNodes)
                {
                    string insert = @"
                        INSERT INTO cad_edges (graph_id, from_node, to_node)
                        VALUES (@graph_id, @from_node, @to_node)";

                    using (var cmd = new SQLiteCommand(insert, connection, transaction))
                    {
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@graph_id", graphId);
                        cmd.Parameters.AddWithValue("@from_node", node.Id);
                        cmd.Parameters.AddWithValue("@to_node", connectedId);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }
    }

    /// <summary>
    /// CAD图形WL数据辅助类
    /// </summary>
    public class CADGraphEdgeWLData
    {
        public List<Dictionary<string, int>> WLFreqs { get; set; } = new List<Dictionary<string, int>>();
        public List<CADRuleData> Rules { get; set; } = new List<CADRuleData>();
    }

    /// <summary>
    /// CAD标注规则数据
    /// </summary>
    public class CADRuleData
    {
        public string RuleName { get; set; }
        public string DimensionValue { get; set; }
        public string DimensionType { get; set; }
        public string AnnotationStyle { get; set; }
        public double Confidence { get; set; }
        public string RuleType { get; set; }

        public CADRuleData(string ruleName, string dimensionValue, string dimensionType,
                          string annotationStyle, double confidence, string ruleType)
        {
            RuleName = ruleName;
            DimensionValue = dimensionValue;
            DimensionType = dimensionType;
            AnnotationStyle = annotationStyle;
            Confidence = confidence;
            RuleType = ruleType;
        }
    }
}
