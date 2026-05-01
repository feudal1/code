using System;
using System.Collections.Generic;
using System.IO; // 用于 Path 操作
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;


namespace tools
{
    public class open_part_by_name
    {
        static public ModelDoc2? run(SldWorks swApp,string arg)
{
  
          
      
       var doc=(ModelDoc2)swApp.OpenDoc(arg, (int)swDocumentTypes_e.swDocPART);
       if(doc==null)
       {
           Console.WriteLine("错误：无法打开零件。");
           return null;
       }
      
       Console.WriteLine("已打开零件：" + arg);
        return doc;
     
        

   
}

}}