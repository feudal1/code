<?php
/**
 * 接收用户消息
 * 
 * POST 参数:
 * - user_id: 用户 ID（可选，自动生成）
 * - message: 消息内容
 * - image: 图片文件（可选）
 */
require_once 'config.php';

if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
    http_response_code(405);
    echo json_encode(['success' => false, 'error' => '仅支持 POST 请求']);
    exit;
}

$conn = getDbConnection();

try {
    // 获取用户 ID
    $user_id = $_POST['user_id'] ?? uniqid('user_');
    $message_text = $_POST['message'] ?? '';
    
    if (empty($message_text)) {
        http_response_code(400);
        echo json_encode(['success' => false, 'error' => '消息内容不能为空']);
        exit;
    }
    
    // 处理上传的图片
    $image_data = null;
    if (isset($_FILES['image']) && $_FILES['image']['error'] === UPLOAD_ERR_OK) {
        $image_tmp = $_FILES['image']['tmp_name'];
        $image_type = $_FILES['image']['type'];
        
        // 验证图片类型
        $allowed_types = ['image/jpeg', 'image/png', 'image/gif', 'image/webp'];
        if (!in_array($image_type, $allowed_types)) {
            http_response_code(400);
            echo json_encode(['success' => false, 'error' => '不支持的图片格式']);
            exit;
        }
        
        // 读取图片并转为 base64
        $image_content = file_get_contents($image_tmp);
        $image_base64 = base64_encode($image_content);
        $image_data = 'data:' . $image_type . ';base64,' . $image_base64;
    }
    
    // 插入或更新用户
    $stmt = $conn->prepare("INSERT INTO users (user_id, username) VALUES (?, ?) ON DUPLICATE KEY UPDATE last_active=CURRENT_TIMESTAMP");
    $username = $_POST['username'] ?? '匿名用户';
    $stmt->bind_param("ss", $user_id, $username);
    $stmt->execute();
    $stmt->close();
    
    // 插入消息
    $stmt = $conn->prepare("INSERT INTO chat_messages (user_id, message_text, image_data, status) VALUES (?, ?, ?, 'pending')");
    $stmt->bind_param("sss", $user_id, $message_text, $image_data);
    
    if ($stmt->execute()) {
        $message_id = $conn->insert_id;
        echo json_encode([
            'success' => true,
            'message_id' => $message_id,
            'status' => 'pending',
            'message' => '消息已发送，等待处理'
        ]);
    } else {
        throw new Exception("保存消息失败");
    }
    
    $stmt->close();
    $conn->close();
    
} catch (Exception $e) {
    http_response_code(500);
    echo json_encode(['success' => false, 'error' => $e->getMessage()]);
    if (isset($conn)) $conn->close();
}
?>
