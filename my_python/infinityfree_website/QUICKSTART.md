# 🚀 InfinityFree 异步消息系统 - 5 分钟快速上手

## 第一步：部署网站（3 分钟）

### 1. 注册 InfinityFree
- 访问 https://infinityfree.net
- 注册账号并创建网站
- 记住你的域名：`your-domain.infinityfreeapp.com`

### 2. 上传文件
```bash
# 将 infinityfree_website 文件夹中的所有文件上传到 htdocs 目录
- config.php
- database.sql
- receive.php
- check_status.php
- get_pending_messages.php
- submit_response.php
- index.html
- check_config.php
```

### 3. 配置数据库
编辑 `config.php`:
```php
define('DB_HOST', 'sql123.infinityfree.com');  // 从控制面板复制
define('DB_USER', 'if0_12345678');             // 从控制面板复制
define('DB_PASS', 'your_password');            // 你的密码
define('DB_NAME', 'epiz_12345678_mydb');       // 从控制面板复制
```

### 4. 创建数据表
- 打开 phpMyAdmin（在 InfinityFree 控制面板）
- 选择你的数据库
- 执行 `database.sql` 中的 SQL 语句

### 5. 验证安装
访问：`https://your-domain.infinityfreeapp.com/check_config.php`
确保所有检查都显示 ✅

## 第二步：配置电脑端（1 分钟）

编辑 `ptools/my_llm/async_processor.py`:
```python
# 第 19 行，修改为你的网站地址
BASE_URL = "https://your-domain.infinityfreeapp.com"
```

## 第三步：测试系统（1 分钟）

### 1. 发送测试消息
- 访问你的网站
- 输入："你好，这是测试消息"
- 点击发送
- 确认显示成功提示

### 2. 处理消息
```bash
# 打开命令行
cd e:\code\my_python

# 运行处理器
python ptools\my_llm\async_processor.py

# 选择选项 1
你的选择：1
```

### 3. 查看结果
- 回到网站
- 点击 "刷新状态" 按钮
- 应该能看到 AI 的回复

## ✅ 完成！

现在你可以：
- 随时发送消息到网站
- 有空时运行处理器处理积压的消息
- 处理后刷新网站查看回复

## 💡 常用操作

### 发送消息
```
1. 打开网站
2. 输入问题或上传图片
3. 点击发送
4. 可以关闭页面去做其他事情
```

### 处理消息
```bash
# 运行处理器
python ptools\my_llm\async_processor.py

# 选择处理方式:
[1] 处理所有待处理消息 (最多 50 条)
[2] 处理前 10 条
[3] 处理前 20 条
[s] 查看统计
[q] 退出
```

### 查看回复
```
1. 回到网站
2. 点击 "刷新状态"
3. 查看 AI 回复
```

## 🎯 系统特点

✅ **24 小时接收**: 网站随时接收消息  
✅ **灵活处理**: 电脑端可选择合适时间处理  
✅ **本地 AI**: 使用 Ollama，无需联网 API  
✅ **零成本**: InfinityFree 完全免费  
✅ **支持图片**: 可上传图片进行分析  

## ⚠️ 注意事项

1. **首次使用需要 Ollama**:
   ```bash
   # 确保 Ollama 服务运行
   ollama serve
   
   # 如果没有模型，先下载
   ollama pull qwen3-vl:8b
   ```

2. **InfinityFree 限制**:
   - 每日访问量约 5000 次
   - 存储空间有限
   - 适合个人和小型项目

3. **定期维护**:
   - 定期清理已完成的消息
   - 备份数据库数据

## 🔧 快速故障排查

### 问题：无法发送消息
**解决**: 
1. 检查 `check_config.php` 
2. 确认数据库配置正确
3. 查看浏览器控制台错误

### 问题：Python 处理器报错
**解决**:
1. 检查 `BASE_URL` 是否正确
2. 确认 Ollama 服务正在运行
3. 在浏览器测试 API 链接

### 问题：看不到回复
**解决**:
1. 确认处理器已处理消息
2. 点击 "刷新状态" 按钮
3. 清除浏览器缓存试试

---

**开始使用吧！** 🎉
