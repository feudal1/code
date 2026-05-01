<?php
/**
 * 配置检查工具
 * 用于测试数据库连接和系统状态
 */
require_once 'config.php';

echo "<!DOCTYPE html>";
echo "<html lang='zh-CN'>";
echo "<head>";
echo "<meta charset='UTF-8'>";
echo "<meta name='viewport' content='width=device-width, initial-scale=1.0'>";
echo "<title>系统配置检查</title>";
echo "<style>";
echo "body { font-family: Arial, sans-serif; padding: 20px; background: #f5f5f5; }";
echo ".container { max-width: 800px; margin: 0 auto; background: white; padding: 30px; border-radius: 10px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }";
echo "h1 { color: #667eea; }";
echo ".status { padding: 15px; margin: 10px 0; border-radius: 5px; }";
echo ".success { background: #d4edda; color: #155724; border: 1px solid #c3e6cb; }";
echo ".error { background: #f8d7da; color: #721c24; border: 1px solid #f5c6cb; }";
echo ".warning { background: #fff3cd; color: #856404; border: 1px solid #ffeaa7; }";
echo ".info { background: #d1ecf1; color: #0c5460; border: 1px solid #bee5eb; }";
echo "table { width: 100%; border-collapse: collapse; margin: 20px 0; }";
echo "th, td { padding: 12px; text-align: left; border-bottom: 1px solid #ddd; }";
echo "th { background: #667eea; color: white; }";
echo "</style>";
echo "</head>";
echo "<body>";
echo "<div class='container'>";
echo "<h1>🔍 InfinityFree 异步消息系统 - 配置检查</h1>";

// 检查 1: PHP 版本
echo "<h2>1️⃣ PHP 环境</h2>";
$php_version = phpversion();
if (version_compare($php_version, '7.0.0', '>=')) {
    echo "<div class='status success'>✅ PHP 版本：{$php_version} (满足要求)</div>";
} else {
    echo "<div class='status error'>❌ PHP 版本：{$php_version} (需要 7.0+)</div>";
}

// 检查 2: 数据库连接
echo "<h2>2️⃣ 数据库连接</h2>";
try {
    $conn = getDbConnection();
    echo "<div class='status success'>✅ 数据库连接成功</div>";
    
    // 显示数据库信息
    $result = $conn->query("SELECT DATABASE() as db_name");
    $row = $result->fetch_assoc();
    $db_name = $row['db_name'];
    
    echo "<table>";
    echo "<tr><th>配置项</th><th>值</th></tr>";
    echo "<tr><td>数据库主机</td><td>" . DB_HOST . "</td></tr>";
    echo "<tr><td>数据库用户名</td><td>" . DB_USER . "</td></tr>";
    echo "<tr><td>数据库名称</td><td>{$db_name}</td></tr>";
    echo "<tr><td>字符集</td><td>" . $conn->character_set_name() . "</td></tr>";
    echo "</table>";
    
    // 检查表是否存在
    echo "<h3>数据表状态</h3>";
    $tables_needed = ['chat_messages', 'users'];
    $result = $conn->query("SHOW TABLES");
    $existing_tables = [];
    while ($row = $result->fetch_array()) {
        $existing_tables[] = $row[0];
    }
    
    foreach ($tables_needed as $table) {
        if (in_array($table, $existing_tables)) {
            echo "<div class='status success'>✅ 表 {$table} 存在</div>";
            
            // 显示记录数
            $count_result = $conn->query("SELECT COUNT(*) as count FROM {$table}");
            $count_row = $count_result->fetch_assoc();
            echo "<div class='status info' style='margin-top:5px;'>📊 记录数：{$count_row['count']}</div>";
        } else {
            echo "<div class='status error'>❌ 表 {$table} 不存在 - 请执行 database.sql 创建表</div>";
        }
    }
    
    $conn->close();
} catch (Exception $e) {
    echo "<div class='status error'>❌ 数据库连接失败：" . $e->getMessage() . "</div>";
    echo "<div class='status warning'>💡 请检查 config.php 中的数据库配置是否正确</div>";
}

// 检查 3: 文件权限
echo "<h2>3️⃣ 文件检查</h2>";
$files_to_check = [
    'config.php' => '配置文件',
    'receive.php' => '接收接口',
    'check_status.php' => '状态查询',
    'get_pending_messages.php' => '消息获取',
    'submit_response.php' => '结果提交',
    'index.html' => '前端页面'
];

foreach ($files_to_check as $file => $desc) {
    if (file_exists($file)) {
        echo "<div class='status success'>✅ {$desc} ({$file}) 存在</div>";
    } else {
        echo "<div class='status error'>❌ {$desc} ({$file}) 不存在</div>";
    }
}

// 检查 4: API 测试
echo "<h2>4️⃣ API 功能测试</h2>";

// 测试获取待处理消息
echo "<h3>测试 get_pending_messages.php</h3>";
try {
    $test_url = "get_pending_messages.php?limit=1";
    $test_data = file_get_contents($test_url);
    $test_result = json_decode($test_data, true);
    
    if ($test_result && isset($test_result['success'])) {
        echo "<div class='status success'>✅ API 响应正常</div>";
        echo "<pre style='background:#f8f9fa;padding:15px;border-radius:5px;'>";
        echo json_encode($test_result, JSON_PRETTY_PRINT | JSON_UNESCAPED_UNICODE);
        echo "</pre>";
    } else {
        throw new Exception("API 返回异常");
    }
} catch (Exception $e) {
    echo "<div class='status error'>❌ API 测试失败：" . $e->getMessage() . "</div>";
}

echo "<h2>5️⃣ 系统信息</h2>";
echo "<table>";
echo "<tr><th>项目</th><th>值</th></tr>";
echo "<tr><td>服务器时间</td><td>" . date('Y-m-d H:i:s') . "</td></tr>";
echo "<tr><td>服务器地址</td><td>" . ($_SERVER['HTTP_HOST'] ?? '未知') . "</td></tr>";
echo "<tr><td>PHP 路径</td><td>" . __FILE__ . "</td></tr>";
echo "<tr><td>最大上传大小</td><td>" . ini_get('upload_max_filesize') . "</td></tr>";
echo "<tr><td>POST 最大大小</td><td>" . ini_get('post_max_size') . "</td></tr>";
echo "</table>";

echo "<hr>";
echo "<h2>✅ 检查完成</h2>";
echo "<div class='status info'>";
echo "💡 <strong>提示:</strong><br>";
echo "1. 如果所有检查通过，系统可以正常使用<br>";
echo "2. 如果有红色错误，请先修复相关问题<br>";
echo "3. 电脑端使用时，请将 BASE_URL 设置为：<strong>https://" . ($_SERVER['HTTP_HOST'] ?? 'your-domain') . "</strong><br>";
echo "</div>";

echo "</div>";
echo "</body>";
echo "</html>";
?>
