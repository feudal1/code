using System;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace tools
{
    public class get_all_body_names
    {
        static public double run(ModelDoc2 swModel)
        {
            try
            {
                if (swModel == null)
                {
                    Console.WriteLine("错误：当前没有活动的 SolidWorks 文档。");
                    return 1;
                }

                PartDoc partDoc = (PartDoc)swModel;
                object[] vBodies = (object[])partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false);
                
                if (vBodies == null || vBodies.Length == 0)
                {
                    Console.WriteLine("当前零件中没有实体 body。");
                    return 0;
                }

                Body2[] bodies = new Body2[vBodies.Length];
                for (int i = 0; i < vBodies.Length; i++)
                {
                    bodies[i] = (Body2)vBodies[i];
                }

                Console.WriteLine($"\n找到 {bodies.Length} 个 body：\n");
                
                foreach (Body2 body in bodies)
                {
                    string bodyName = body.Name;
                    Console.WriteLine($"  - {bodyName}");
                }

                Console.WriteLine($"\n总计：{bodies.Length} 个 body");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误：{ex.Message}");
                Console.WriteLine("提示：请确保 SolidWorks 正在运行且当前打开的是零件文档。");
            }

            return 0;
        }
    }
}