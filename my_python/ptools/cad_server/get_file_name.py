import os
from pyautocad import Autocad

acad = Autocad(create_if_not_exists=True)
doc_name = acad.doc.Name

# 移除扩展名
if '.' in doc_name:
    base_name = ".".join(doc_name.split('.')[:-1])
else:
    base_name = doc_name

print(f"基础文件名: {base_name}")