<?php
/**
 * 检查消息处理状态
 * 
 * GET 参数:
 * - id: 消息 ID
 */
require_once 'config.php';

$message_id = $_GET['id'] ?? null;

if (!$message_id) {
    http_response_code(400);
    echo json_encode(['success' => false, 'error' => '缺少消息 ID']);
    exit;
}

$conn = getDbConnection();

try {
    $stmt = $conn->prepare("SELECT status, response_text, processed_at FROM chat_messages WHERE id = ?");
    $stmt->bind_param("i", $message_id);
    $stmt->execute();
    $result = $stmt->get_result();
    
    if ($row = $result->fetch_assoc()) {
        echo json_encode([
            'success' => true,
            'message_id' => $message_id,
            'status' => $row['status'],
            'response' => $row['response_text'],
            'processed_at' => $row['processed_at']
        ]);
    } else {
        http_response_code(404);
        echo json_encode(['success' => false, 'error' => '消息不存在']);
    }
    
    $stmt->close();
    $conn->close();
    
} catch (Exception $e) {
    http_response_code(500);
    echo json_encode(['success' => false, 'error' => $e->getMessage()]);
    if (isset($conn)) $conn->close();
}
?>
