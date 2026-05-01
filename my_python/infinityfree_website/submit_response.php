<?php
/**
 * 提交处理结果（电脑端使用）
 * 
 * POST 参数:
 * - message_id: 消息 ID
 * - response: 回复内容
 * - status: 状态（completed 或 failed）
 */
require_once 'config.php';

if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
    http_response_code(405);
    echo json_encode(['success' => false, 'error' => '仅支持 POST 请求']);
    exit;
}

$conn = getDbConnection();

try {
    $message_id = intval($_POST['message_id']);
    $response_text = $_POST['response'] ?? '';
    $status = $_POST['status'] ?? 'completed';
    
    if (empty($response_text)) {
        http_response_code(400);
        echo json_encode(['success' => false, 'error' => '回复内容不能为空']);
        exit;
    }
    
    // 更新消息状态
    $stmt = $conn->prepare("UPDATE chat_messages SET status = ?, response_text = ?, processed_at = CURRENT_TIMESTAMP WHERE id = ?");
    $stmt->bind_param("ssi", $status, $response_text, $message_id);
    
    if ($stmt->execute() && $stmt->affected_rows > 0) {
        echo json_encode(['success' => true, 'message' => '回复已提交']);
    } else {
        throw new Exception("更新消息失败");
    }
    
    $stmt->close();
    $conn->close();
    
} catch (Exception $e) {
    http_response_code(500);
    echo json_encode(['success' => false, 'error' => $e->getMessage()]);
    if (isset($conn)) $conn->close();
}
?>
