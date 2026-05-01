# file: split_pdf.py
import tkinter as tk
from tkinter import filedialog, messagebox, ttk
import os
from PyPDF2 import PdfReader, PdfWriter
import subprocess
import platform

class PDFSplitterApp:
    def __init__(self, root):
        self.root = root
        self.root.title("PDF拆分工具")
        self.root.geometry("600x550")
        
        self.pdf_file = ""
        self.total_pages = 0
        
        self.create_widgets()
    
    def create_widgets(self):
        # 标题
        title_label = tk.Label(self.root, text="PDF拆分工具", font=("Arial", 16))
        title_label.pack(pady=10)
        
        # 选择文件区域
        file_frame = tk.Frame(self.root)
        file_frame.pack(pady=10, padx=20, fill=tk.X)
        
        tk.Label(file_frame, text="选择PDF文件:").pack(anchor=tk.W)
        
        file_select_frame = tk.Frame(file_frame)
        file_select_frame.pack(fill=tk.X, pady=5)
        
        self.file_path_var = tk.StringVar()
        tk.Entry(file_select_frame, textvariable=self.file_path_var, state="readonly").pack(side=tk.LEFT, fill=tk.X, expand=True)
        
        select_btn = tk.Button(file_select_frame, text="浏览", command=self.select_file)
        select_btn.pack(side=tk.RIGHT, padx=(5, 0))
        
        # 页面信息显示
        self.info_frame = tk.Frame(self.root)
        self.info_frame.pack(pady=10, padx=20, fill=tk.X)
        
        self.page_info_label = tk.Label(self.info_frame, text="请选择PDF文件")
        self.page_info_label.pack(anchor=tk.W)
        
        # 拆分选项区域
        options_frame = tk.LabelFrame(self.root, text="拆分选项")
        options_frame.pack(pady=10, padx=20, fill=tk.X)
        
        # 按页面范围拆分
        range_frame = tk.Frame(options_frame)
        range_frame.pack(fill=tk.X, padx=10, pady=5)
        
        self.range_var = tk.BooleanVar()
        tk.Radiobutton(range_frame, text="按页面范围拆分", variable=self.range_var, value=False, command=self.toggle_options).pack(anchor=tk.W)
        
        range_input_frame = tk.Frame(range_frame)
        range_input_frame.pack(fill=tk.X, padx=(20, 0))
        
        tk.Label(range_input_frame, text="从第").pack(side=tk.LEFT)
        self.start_page_var = tk.StringVar(value="1")
        tk.Entry(range_input_frame, textvariable=self.start_page_var, width=5).pack(side=tk.LEFT)
        tk.Label(range_input_frame, text="页到第").pack(side=tk.LEFT)
        self.end_page_var = tk.StringVar()
        tk.Entry(range_input_frame, textvariable=self.end_page_var, width=5).pack(side=tk.LEFT)
        tk.Label(range_input_frame, text="页").pack(side=tk.LEFT)
        
        # 按页数拆分
        page_count_frame = tk.Frame(options_frame)
        page_count_frame.pack(fill=tk.X, padx=10, pady=5)
        
        tk.Radiobutton(page_count_frame, text="按每份页数拆分", variable=self.range_var, value=True, command=self.toggle_options).pack(anchor=tk.W)
        
        page_count_input_frame = tk.Frame(page_count_frame)
        page_count_input_frame.pack(fill=tk.X, padx=(20, 0))
        
        tk.Label(page_count_input_frame, text="每份").pack(side=tk.LEFT)
        self.pages_per_split_var = tk.StringVar(value="1")
        tk.Entry(page_count_input_frame, textvariable=self.pages_per_split_var, width=5).pack(side=tk.LEFT)
        tk.Label(page_count_input_frame, text="页").pack(side=tk.LEFT)
        
        # 单页拆分选项
        single_page_frame = tk.Frame(options_frame)
        single_page_frame.pack(fill=tk.X, padx=10, pady=5)
        
        self.single_page_var = tk.BooleanVar()
        tk.Radiobutton(single_page_frame, text="拆分成单页", variable=self.single_page_var, value=True, command=self.toggle_single_page).pack(anchor=tk.W)
        
        # 进度条
        self.progress_frame = tk.Frame(self.root)
        self.progress_frame.pack(pady=10, padx=20, fill=tk.X)
        
        self.progress = ttk.Progressbar(self.progress_frame, mode='determinate')
        self.progress.pack(fill=tk.X)
        
        self.progress_label = tk.Label(self.progress_frame, text="")
        self.progress_label.pack()
        
        # 操作按钮
        button_frame = tk.Frame(self.root)
        button_frame.pack(pady=20)
        
        self.split_btn = tk.Button(button_frame, text="拆分PDF", command=self.split_pdf,
                                  bg="#2196F3", fg="white", padx=20, pady=5, state="disabled")
        self.split_btn.pack(side=tk.LEFT, padx=5)
        
        exit_btn = tk.Button(button_frame, text="退出", command=self.root.quit,
                            bg="#f44336", fg="white", padx=20, pady=5)
        exit_btn.pack(side=tk.LEFT, padx=5)
        
        # 默认选择按范围拆分
        self.range_var.set(False)
        self.single_page_var.set(False)
        self.toggle_options()
        self.toggle_single_page()
    
    def toggle_options(self):
        """切换拆分选项"""
        pass  # 保留此方法以便扩展
    
    def toggle_single_page(self):
        """切换单页拆分选项"""
        if self.single_page_var.get():
            # 如果选择了单页拆分，禁用其他选项
            self.range_var.set(False)
    
    def select_file(self):
        """选择PDF文件"""
        file_path = filedialog.askopenfilename(
            title="选择PDF文件",
            filetypes=[("PDF files", "*.pdf"), ("All files", "*.*")]
        )
        
        if file_path:
            self.pdf_file = file_path
            self.file_path_var.set(file_path)
            
            # 获取PDF页数
            try:
                reader = PdfReader(file_path)
                self.total_pages = len(reader.pages)
                self.page_info_label.config(text=f"总页数: {self.total_pages}")
                self.end_page_var.set(str(self.total_pages))
                self.split_btn.config(state="normal")
            except Exception as e:
                messagebox.showerror("错误", f"无法读取PDF文件:\n{str(e)}")
                self.pdf_file = ""
                self.file_path_var.set("")
                self.page_info_label.config(text="请选择PDF文件")
                self.split_btn.config(state="disabled")
    
    def split_pdf(self):
        """拆分PDF文件"""
        if not self.pdf_file:
            messagebox.showwarning("警告", "请先选择PDF文件！")
            return
        
        try:
            reader = PdfReader(self.pdf_file)
            total_pages = len(reader.pages)
            
            # 单页拆分
            if self.single_page_var.get():
                # 选择保存目录
                output_dir = filedialog.askdirectory(title="选择保存拆分文件的目录")
                if not output_dir:
                    return
                
                # 显示进度条
                self.progress["maximum"] = total_pages
                self.progress["value"] = 0
                
                base_name = os.path.splitext(os.path.basename(self.pdf_file))[0]
                
                for i in range(total_pages):
                    writer = PdfWriter()
                    writer.add_page(reader.pages[i])
                    
                    # 生成文件名：原文件名_页码.pdf
                    output_filename = os.path.join(output_dir, f"{base_name}_{i+1}.pdf")
                    with open(output_filename, "wb") as output_file:
                        writer.write(output_file)
                    
                    # 更新进度
                    self.progress["value"] = i + 1
                    self.progress_label.config(text=f"正在处理: {i+1}/{total_pages} 页")
                    self.root.update_idletasks()
                
                self.progress_label.config(text="拆分完成!")
                messagebox.showinfo("成功", f"PDF已拆分成{total_pages}个单页文件!\n保存在: {output_dir}")
                
            elif self.range_var.get():  # 按每份页数拆分
                pages_per_split = int(self.pages_per_split_var.get())
                if pages_per_split <= 0:
                    raise ValueError("每份页数必须大于0")
                
                # 计算需要拆分成多少份
                num_splits = (total_pages + pages_per_split - 1) // pages_per_split
                
                # 选择保存目录
                output_dir = filedialog.askdirectory(title="选择保存拆分文件的目录")
                if not output_dir:
                    return
                
                # 显示进度条
                self.progress["maximum"] = num_splits
                self.progress["value"] = 0
                
                base_name = os.path.splitext(os.path.basename(self.pdf_file))[0]
                
                for i in range(num_splits):
                    start_page = i * pages_per_split
                    end_page = min((i + 1) * pages_per_split, total_pages)
                    
                    writer = PdfWriter()
                    for j in range(start_page, end_page):
                        writer.add_page(reader.pages[j])
                    
                    output_filename = os.path.join(output_dir, f"{base_name}_part{i+1}.pdf")
                    with open(output_filename, "wb") as output_file:
                        writer.write(output_file)
                    
                    # 更新进度
                    self.progress["value"] = i + 1
                    self.progress_label.config(text=f"正在处理: {i+1}/{num_splits}")
                    self.root.update_idletasks()
                
                self.progress_label.config(text="拆分完成!")
                messagebox.showinfo("成功", f"PDF已拆分成{num_splits}个文件!\n保存在: {output_dir}")
                
            else:  # 按页面范围拆分
                start_page = int(self.start_page_var.get()) - 1  # 转换为0索引
                end_page = int(self.end_page_var.get())  # 保持原样，因为是包含的结束页
                
                if start_page < 0 or end_page > total_pages or start_page >= end_page:
                    raise ValueError("页面范围无效")
                
                # 选择保存路径
                output_path = filedialog.asksaveasfilename(
                    title="保存拆分后的PDF",
                    defaultextension=".pdf",
                    filetypes=[("PDF files", "*.pdf"), ("All files", "*.*")]
                )
                
                if output_path:
                    writer = PdfWriter()
                    for i in range(start_page, end_page):
                        writer.add_page(reader.pages[i])
                    
                    with open(output_path, "wb") as output_file:
                        writer.write(output_file)
                    
                    self.progress_label.config(text="拆分完成!")
                    # 打开文件
                    self.open_file(output_path)
                    messagebox.showinfo("成功", f"PDF拆分完成!\n保存路径: {output_path}")
            
        except Exception as e:
            messagebox.showerror("错误", f"拆分过程中出现错误:\n{str(e)}")
        finally:
            self.progress["value"] = 0
    
    def open_file(self, file_path):
        """根据操作系统打开文件"""
        try:
            system = platform.system()
            if system == "Windows":
                os.startfile(file_path)
            elif system == "Darwin":  # macOS
                subprocess.call(["open", file_path])
            elif system == "Linux":
                subprocess.call(["xdg-open", file_path])
        except Exception as e:
            messagebox.showerror("错误", f"无法打开文件:\n{str(e)}")

def main():
    root = tk.Tk()
    app = PDFSplitterApp(root)
    root.mainloop()

if __name__ == "__main__":
    main()