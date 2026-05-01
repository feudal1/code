# InfinityFree 异步消息系统 - 部署指南

## 📋 系统架构

```
用户发送消息 → InfinityFree 网站 → MySQL 数据库存储
                                          ↓
电脑端处理 ← 主动获取待处理消息 ← MySQL 数据库
   ↓
本地 Ollama 处理
   ↓
提交回复 → MySQL 数据库 → 用户刷新查看
```

## 🌐 InfinityFree 部署步骤

### 1. 注册 InfinityFree 账号

1. 访问 https://infinityfree.net
2. 点击 "Sign Up" 注册免费账号
3. 验证邮箱并登录
4. 点击 "Create Account" 创建新网站
5. 选择子域名（如：yourname.infinityfreeapp.com）

### 2. 设置数据库

1. 进入控制面板 → **MySQL Databases**
2. 点击 "Create New Database"
3. 记录以下关键信息：
   - **数据库主机**: 如 `sql123.infinityfree.com`
   - **数据库用户名**: 如 `if0_12345678`
   - **数据库密码**: 你设置的密码
   - **数据库名称**: 如 `epiz_12345678_mydb`

### 3. 上传网站文件

#### 方法一：使用 FileZilla（推荐）

1. 下载并安装 [FileZilla Client](https://filezilla-project.org/)
2. 在 InfinityFree 控制面板找到 FTP 信息：
   - **FTP 主机**: 如 `ftpupload.net`
   - **FTP 用户名**: 你的 InfinityFree 用户名
   - **FTP 密码**: 你的密码
   - **端口**: 21

3. 连接后，进入 `htdocs` 目录
4. 将 `infinityfree_website` 文件夹中的所有文件上传到 `htdocs`

#### 方法二：使用在线文件管理器

1. 在控制面板点击 "File Manager"
2. 进入 `htdocs` 目录
3. 逐个上传文件

### 4. 配置数据库连接

编辑 `config.php` 文件，修改数据库配置：

```php
define('DB_HOST', 'sql123.infinityfree.com'); // 你的数据库主机
define('DB_USER', 'if0_12345678'); // 你的数据库用户名
define('DB_PASS', 'your_password'); // 你的数据库密码
define('DB_NAME', 'epiz_12345678_mydb'); // 你的数据库名
```

### 5. 创建数据表

1. 在控制面板点击 **phpMyAdmin**
2. 选择你的数据库
3. 点击 "SQL" 标签
4. 复制 `database.sql` 中的 SQL 语句并执行
5. 确认创建了 `chat_messages` 和 `users` 两个表

### 6. 测试网站

1. 访问你的网站：`https://your-domain.infinityfreeapp.com`
2. 输入一条测试消息并发送
3. 确认消息成功发送（显示成功提示）

## 💻 电脑端配置

### 1. 配置处理器

编辑 `ptools/my_llm/async_processor.py` 文件：

```python
# 修改这行为你的网站地址
BASE_URL = "https://your-domain.infinityfreeapp.com"
```

### 2. 确保 Ollama 服务运行

```bash
ollama serve
```

确认模型已安装：
```bash
ollama list
```

如果没有需要的模型，先下载：
```bash
ollama pull qwen3-vl:8b
```

### 3. 运行处理器

```bash
cd e:\code\my_python
python ptools\my_llm\async_processor.py
```

### 4. 使用方法

程序启动后会显示交互式菜单：

```
请选择操作:
  [1] 检查并处理所有待处理消息
  [2] 检查并处理前 10 条消息
  [3] 检查并处理前 20 条消息
  [s] 查看统计信息
  [q] 退出

你的选择：
```

- 输入 `1` 处理所有积压的消息
- 输入 `2` 或 `3` 处理部分消息
- 输入 `s` 查看处理统计
- 输入 `q` 退出程序

## 🎯 使用流程

### 用户端（手机/电脑浏览器）

1. **发送消息**
   - 打开网站
   - 输入问题或上传图片
   - 点击发送
   
2. **等待处理**
   - 可以关闭页面做其他事情
   - 消息已在后台队列中
   
3. **查看回复**
   - 稍后回到网站
   - 点击 "刷新状态" 按钮
   - 查看 AI 的回复

### 电脑端（本地处理）

1. **启动处理器**
   ```bash
   python ptools\my_llm\async_processor.py
   ```

2. **处理消息**
   - 输入 `1` 开始处理
   - 程序自动获取待处理消息
   - 调用本地 Ollama 进行 AI 处理
   - 自动提交回复到数据库

3. **停止处理**
   - 按 `Ctrl+C` 中断当前处理
   - 输入 `q` 退出程序

## ⚙️ 高级配置

### 调整单次处理数量

在交互式菜单中选择不同选项：
- 选项 1: 最多 50 条
- 选项 2: 最多 10 条
- 选项 3: 最多 20 条

### 修改默认限制

编辑 `get_pending_messages.php`:
```php
$limit = intval($_GET['limit'] ?? 50); // 改为其他数字
if ($limit > 200) $limit = 200; // 最大限制
```

### 添加简单密码保护（可选）

如果需要保护管理功能，可以在 `get_pending_messages.php` 和 `submit_response.php` 中添加简单的密码验证：

```php
// 在文件开头添加
$password = $_GET['key'] ?? '';
if ($password !== 'your_secret_key') {
    http_response_code(403);
    echo json_encode(['success' => false, 'error' => '未授权访问']);
    exit;
}
```

然后在 Python 中调用时添加参数：
```python
url = f"{BASE_URL}/get_pending_messages.php?key=your_secret_key"
```

## 📊 数据库维护

### 清理已完成的消息

定期清理已完成的消息以节省空间：

```sql
-- 删除 30 天前已处理的消息
DELETE FROM chat_messages 
WHERE status = 'completed' 
AND processed_at < DATE_SUB(NOW(), INTERVAL 30 DAY);
```

### 查看统计数据

```sql
-- 查看各状态消息数量
SELECT status, COUNT(*) as count 
FROM chat_messages 
GROUP BY status;

-- 查看总用户数
SELECT COUNT(*) as total_users FROM users;

-- 查看今日新增消息
SELECT COUNT(*) as today_messages 
FROM chat_messages 
WHERE DATE(created_at) = CURDATE();
```

## 🔧 故障排查

### 问题 1: 无法连接到数据库

**症状**: PHP 返回数据库连接错误

**解决**:
1. 检查 `config.php` 中的配置是否正确
2. 确认数据库已创建
3. 在 phpMyAdmin 中测试能否登录

### 问题 2: 上传文件失败

**症状**: 图片上传失败或 413 错误

**解决**:
1. InfinityFree 有文件大小限制（通常 10MB）
2. 压缩图片或限制上传尺寸
3. 检查 `php.ini` 设置（如果支持）

### 问题 3: Python 处理器无法获取消息

**症状**: 网络错误或空响应

**解决**:
1. 检查 `BASE_URL` 是否正确
2. 在浏览器测试 API: `https://your-domain.infinityfreeapp.com/get_pending_messages.php`
3. 检查防火墙设置

### 问题 4: Ollama 调用失败

**症状**: 处理器报错无法调用模型

**解决**:
1. 确认 `ollama serve` 正在运行
2. 检查模型是否存在：`ollama list`
3. 重新下载模型：`ollama pull qwen3-vl:8b`

## 📈 性能优化建议

1. **批量处理**: 一次处理多条消息减少 HTTP 请求
2. **图片优化**: 限制上传图片大小和质量
3. **数据库索引**: 已自动添加，不要删除
4. **定期清理**: 删除旧的已完成消息

## 🎯 优势总结

✅ **零成本**: InfinityFree 完全免费
✅ **24 小时在线**: 网站随时接收消息
✅ **灵活处理**: 电脑端可选择合适时间处理
✅ **本地 AI**: 使用 Ollama，无需联网调用 API
✅ **简单易用**: 无需复杂配置
✅ **可扩展**: 支持多用户并发

## 📝 注意事项

1. **InfinityFree 限制**:
   - 每日访问量约 5000 次
   - 存储空间有限
   - 不适合高并发场景

2. **数据安全**:
   - 定期备份数据库
   - 不要在数据库中存储敏感信息

3. **升级方案**:
   - 如果访问量增加，考虑付费主机
   - 可迁移到 VPS 获得更好性能

## 🆘 获取帮助

遇到问题可以：
1. 查看本文档的故障排查部分
2. 检查 InfinityFree 控制面板的错误日志
3. 查看 Python 处理器的详细错误输出

---

**祝你使用愉快！** 🎉
