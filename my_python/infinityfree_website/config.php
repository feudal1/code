<?php
/**
 * InfinityFree 异步消息系统配置文件
 * 
 * 数据库配置（在 InfinityFree 控制面板获取这些信息）
 */
header('Content-Type: application/json; charset=utf-8');
header('Access-Control-Allow-Origin: *');
header('Access-Control-Allow-Methods: POST, GET, OPTIONS');
header('Access-Control-Allow-Headers: Content-Type');

// 处理预检请求
if ($_SERVER['REQUEST_METHOD'] === 'OPTIONS') {
    exit(0);
}

// 数据库配置
define('DB_HOST', 'sql208.infinityfree.com'); // 替换为你的数据库主机
define('DB_USER', 'if0_41574228'); // 替换为你的数据库用户名
define('DB_PASS', 'EcRgMoJsWhlGbF'); // 替换为你的数据库密码
define('DB_NAME', 'if0_41574228_feudal'); // 替换为你的数据库名

/**
 * 获取数据库连接
 */
function getDbConnection() {
    try {
        $conn = new mysqli(DB_HOST, DB_USER, DB_PASS, DB_NAME);
        if ($conn->connect_error) {
            throw new Exception("数据库连接失败：" . $conn->connect_error);
        }
        $conn->set_charset("utf8mb4");
        return $conn;
    } catch (Exception $e) {
        http_response_code(500);
        echo json_encode(['success' => false, 'error' => $e->getMessage()]);
        exit;
    }
}
?>
