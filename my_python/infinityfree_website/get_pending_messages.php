<?php
/**
 * 获取所有待处理的消息（电脑端使用）
 * 
 * GET 参数:
 * - limit: 返回数量限制（可选，默认 50）
 */
require_once 'config.php';

$limit = intval($_GET['limit'] ?? 50);
if ($limit <= 0) $limit = 50;
if ($limit > 200) $limit = 200; // 最大返回 200 条

$conn = getDbConnection();

try {
    $stmt = $conn->prepare("SELECT id, user_id, message_text, image_data, created_at FROM chat_messages WHERE status = 'pending' ORDER BY created_at ASC LIMIT ?");
    $stmt->bind_param("i", $limit);
    $stmt->execute();
    $result = $stmt->get_result();
    
    $messages = [];
    while ($row = $result->fetch_assoc()) {
        $messages[] = [
            'id' => $row['id'],
            'user_id' => $row['user_id'],
            'message' => $row['message_text'],
            'image' => $row['image_data'],
            'created_at' => $row['created_at']
        ];
    }
    
    echo json_encode([
        'success' => true,
        'count' => count($messages),
        'messages' => $messages
    ]);
    
    $stmt->close();
    $conn->close();
    
} catch (Exception $e) {
    http_response_code(500);
    echo json_encode(['success' => false, 'error' => $e->getMessage()]);
    if (isset($conn)) $conn->close();
}
?>
