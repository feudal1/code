# InfinityFree 异步消息系统 - 项目说明

## 📁 项目结构

```
e:\code\my_python\
├── infinityfree_website/          # InfinityFree 网站代码（上传到托管）
│   ├── config.php                 # 数据库配置文件
│   ├── database.sql               # 数据库表结构
│   ├── receive.php                # 接收用户消息接口
│   ├── check_status.php           # 检查消息状态接口
│   ├── get_pending_messages.php   # 获取待处理消息接口
│   ├── submit_response.php        # 提交处理结果接口
│   ├── index.html                 # 用户前端页面
│   ├── check_config.php           # 系统配置检查工具
│   ├── README.md                  # 详细部署指南
│   └── QUICKSTART.md              # 快速上手指南
│
└── ptools\my_llm\
    └── async_processor.py         # 电脑端消息处理器
```

## 🎯 系统架构

```
┌─────────────┐      ┌──────────────────┐      ┌──────────────┐
│   用户端    │ ───> │ InfinityFree 网站 │ ───> │ MySQL 数据库 │
│ (手机/电脑) │      │  (24 小时在线)   │      │  (存储消息)  │
└─────────────┘      └──────────────────┘      └──────┬───────┘
                                                      │
                                                      ↓
┌─────────────┐      ┌──────────────────┐      ┌──────┴───────┐
│  用户查看   │ <─── │ InfinityFree 网站 │ <─── │ MySQL 数据库 │
│  AI 回复     │      │  (显示结果)      │      │  (已处理)    │
└─────────────┘      └──────────────────┘      └──────────────┘
       ▲                                              │
       │                                              ↓
       │                                     ┌───────┴────────┐
       └─────────────────────────────────────│  电脑端处理器  │
              主动查询                       │ (本地 Ollama)  │
                                             └────────────────┘
```

## 🔄 工作流程

### 1. 用户发送消息
```
用户 → 网站输入消息 → 保存到 MySQL → 返回消息 ID
```

### 2. 电脑端处理（用户主动触发）
```
运行处理器 → 从 MySQL 获取待处理消息 → 调用 Ollama → 保存回复到 MySQL
```

### 3. 用户查看回复
```
访问网站 → 点击刷新 → 从 MySQL 读取回复 → 显示给用户
```

## 📋 核心特性

### ✅ 收发分离
- **接收端**: InfinityFree 网站 24 小时在线
- **处理端**: 电脑端本地 Ollama，可选择合适时间处理

### ✅ 无需轮询
- 用户主动查询待处理消息
- 电脑端按需启动处理器
- 节省资源，避免无效请求

### ✅ 独立部署
- InfinityFree 代码在独立文件夹
- 清晰的边界，易于维护
- 可单独更新任一部分

### ✅ 完整功能
- 支持文本对话
- 支持图片上传分析
- 消息状态跟踪
- 用户历史记录

## 🚀 快速开始

### 步骤 1: 部署网站
```bash
# 1. 注册 InfinityFree
# 2. 上传 infinityfree_website 所有文件到 htdocs
# 3. 修改 config.php 中的数据库配置
# 4. 执行 database.sql 创建表
# 5. 访问 check_config.php 验证安装
```

### 步骤 2: 配置处理器
```python
# 编辑 ptools/my_llm/async_processor.py
BASE_URL = "https://your-domain.infinityfreeapp.com"
```

### 步骤 3: 测试
```bash
# 1. 网站发送测试消息
# 2. 运行处理器
python ptools\my_llm\async_processor.py
# 3. 选择选项 1 处理消息
# 4. 网站刷新查看回复
```

## 💻 使用方法

### 用户端（网站）
1. 访问网站
2. 输入问题或上传图片
3. 点击发送
4. 稍后回来点击"刷新状态"
5. 查看 AI 回复

### 电脑端（处理器）
```bash
# 运行处理器
python ptools\my_llm\async_processor.py

# 交互式菜单:
[1] 处理所有待处理消息 (最多 50 条)
[2] 处理前 10 条消息
[3] 处理前 20 条消息
[s] 查看统计信息
[q] 退出程序
```

## 🔧 API 接口说明

### 1. receive.php - 接收消息
**方法**: POST  
**参数**:
- `user_id`: 用户 ID（可选）
- `message`: 消息内容（必填）
- `image`: 图片文件（可选）

**响应**:
```json
{
  "success": true,
  "message_id": 123,
  "status": "pending"
}
```

### 2. check_status.php - 检查状态
**方法**: GET  
**参数**:
- `id`: 消息 ID

**响应**:
```json
{
  "success": true,
  "status": "completed",
  "response": "AI 的回复内容"
}
```

### 3. get_pending_messages.php - 获取待处理消息
**方法**: GET  
**参数**:
- `limit`: 数量限制（可选，默认 50）

**响应**:
```json
{
  "success": true,
  "count": 5,
  "messages": [
    {
      "id": 123,
      "user_id": "user_xxx",
      "message": "消息内容",
      "image": "base64...",
      "created_at": "2026-04-04 10:00:00"
    }
  ]
}
```

### 4. submit_response.php - 提交回复
**方法**: POST  
**参数**:
- `message_id`: 消息 ID（必填）
- `response`: 回复内容（必填）
- `status`: 状态（completed/failed）

**响应**:
```json
{
  "success": true,
  "message": "回复已提交"
}
```

## 🗄️ 数据库设计

### chat_messages 表
| 字段 | 类型 | 说明 |
|------|------|------|
| id | INT | 主键 |
| user_id | VARCHAR(100) | 用户 ID |
| message_text | TEXT | 消息内容 |
| image_data | LONGTEXT | 图片 base64 |
| status | ENUM | pending/processing/completed/failed |
| response_text | LONGTEXT | AI 回复 |
| created_at | DATETIME | 创建时间 |
| processed_at | DATETIME | 处理完成时间 |

### users 表
| 字段 | 类型 | 说明 |
|------|------|------|
| user_id | VARCHAR(100) | 主键 |
| username | VARCHAR(255) | 用户名 |
| created_at | DATETIME | 注册时间 |
| last_active | DATETIME | 最后活跃时间 |

## ⚙️ 配置选项

### config.php
```php
define('DB_HOST', 'sql123.infinityfree.com');  // 数据库主机
define('DB_USER', 'if0_12345678');             // 数据库用户
define('DB_PASS', 'your_password');            // 数据库密码
define('DB_NAME', 'epiz_12345678_mydb');       // 数据库名称
```

### async_processor.py
```python
BASE_URL = "https://your-domain.infinityfreeapp.com"  # 网站地址
```

### 环境变量
```bash
OLLAMA_MODEL=qwen3-vl:8b  # 使用的 Ollama 模型
```

## 📊 性能指标

- **单次处理能力**: 最多 200 条消息
- **响应时间**: 取决于 Ollama 模型大小
- **并发支持**: 多用户同时发送
- **存储限制**: InfinityFree 免费空间

## 🔒 安全建议

1. **定期备份数据库**
2. **添加简单密码保护**（可选）
3. **限制图片上传大小**
4. **清理旧消息**

## 🛠️ 故障排查

### 常见问题

#### 1. 无法连接数据库
- 检查 config.php 配置
- 确认数据库已创建
- 在 phpMyAdmin 测试登录

#### 2. Python 报错
- 检查 BASE_URL 是否正确
- 确认 Ollama 服务运行
- 查看详细错误信息

#### 3. 看不到回复
- 确认处理器已处理
- 点击刷新按钮
- 清除浏览器缓存

### 调试工具
- `check_config.php` - 系统健康检查
- 浏览器开发者工具 - 查看网络请求
- Python 日志输出 - 查看处理过程

## 📈 扩展建议

### 功能扩展
- [ ] 用户登录系统
- [ ] 消息优先级队列
- [ ] 批量导出对话
- [ ] 统计分析面板
- [ ] 多语言支持

### 性能优化
- [ ] Redis 缓存
- [ ] 消息分页加载
- [ ] 图片 CDN 存储
- [ ] 数据库读写分离

### 安全加固
- [ ] API 密钥认证
- [ ] IP 访问限制
- [ ] SQL 注入防护
- [ ] XSS 攻击防护

## 📝 更新日志

### v1.0.0 - 2026-04-04
- ✅ 初始版本发布
- ✅ 基础消息收发功能
- ✅ 支持图片上传
- ✅ 电脑端处理器
- ✅ 完整文档

## 🎓 学习资源

- [InfinityFree 官方文档](https://infinityfree.net/support)
- [PHP MySQL 教程](https://www.php.net/manual/zh/)
- [Ollama 使用指南](https://ollama.ai/)
- [Flask Web 开发](https://flask.palletsprojects.com/)

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！

## 📄 许可证

MIT License

---

**祝你使用愉快！** 🎉

如有问题，请查看：
- [README.md](README.md) - 详细部署指南
- [QUICKSTART.md](QUICKSTART.md) - 快速上手指南
- [check_config.php](check_config.php) - 系统检查工具
