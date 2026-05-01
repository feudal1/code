using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using View=SolidWorks.Interop.sldworks.View;

namespace tools
{
    public class get_all_face
    {
        /// <summary>
        /// 获取所有面的信息
        /// </summary>
        static public void run(ModelDoc2 swModel)
        {
            if (swModel == null)
            {
                Console.WriteLine("错误：没有打开的活动文档。");
                return;
            }

            PartDoc partDoc = (PartDoc)swModel;
          
            // 遍历每个视图
             object[] vBodies = (object[])partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false);
                     foreach (Body2 body in vBodies)
        {
            object[] vFaces = (object[])body.GetFaces();
              Console.WriteLine("vFaces.Length=" + vFaces.Length);
              foreach (Face face in vFaces)
              {
               
            
                  var area=Math.Round(face.GetArea()*1000000,2);
                  Console.WriteLine("面积="+area);
 
              }
        }
 
 
          }
      }
    }