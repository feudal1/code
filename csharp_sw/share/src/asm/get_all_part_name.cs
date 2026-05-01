using System;
using System.Collections.Generic;
using System.IO; // 用于 Path 操作
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace tools
{
    public class Getallpartname
    {
        static private Dictionary<string, int> docnameCount = new Dictionary<string, int>();

        static public string[]? run(ModelDoc2 swModel)
{
    try
    {
        if (swModel == null)
        {
            Console.WriteLine("错误：没有打开任何文档。");
            return null;
        }

        // 确保是装配体
        if (swModel.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            Console.WriteLine("错误：当前文档不是装配体。");
            return null;
        }

        AssemblyDoc swAssembly = (AssemblyDoc)swModel;

        // 获取顶层组件
        object[] topComponents = (object[])swAssembly.GetComponents(false);

        Console.WriteLine("正在扫描装配体...");

        // 遍历顶层组件
        foreach (object compObj in topComponents)
        {
            Component2 topComp = (Component2)compObj;
            TraverseComponent(topComp); // 递归遍历组件
        }
                foreach (var kvp in docnameCount)
        {
            Console.WriteLine($"{kvp.Key}: {kvp.Value} 次");
        }
        List<string> result = new List<string>(docnameCount.Keys);
        return result.ToArray();

    }
    catch (Exception ex)
    {
        Console.WriteLine($"发生错误: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
    }
    return null;
}

// 递归遍历组件
static private void TraverseComponent(Component2 component)
{
    // 获取子组件
    object[] children = (object[])component.GetChildren();
 
    if (children != null && children.Length > 0)
    {
  
    }
    else
    {
        var doc=(ModelDoc2)component.GetModelDoc2();
    
        if (doc != null)
        {  string docname=doc.GetPathName().Replace(@"\", "/");
            if (docnameCount.ContainsKey(docname))
        {
            docnameCount[docname]++;
        }
        else
        {
            docnameCount[docname] = 1;
        }
            
        }
      
    }
}   
    }
}