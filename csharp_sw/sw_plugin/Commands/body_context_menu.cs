using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;
using SolidWorksTools;
using System;
using System.Diagnostics;
using SolidWorks.Interop.swconst;
using System.Collections.Generic;
using System.IO;
using tools;
namespace SolidWorksAddinStudy
{
    /// <summary>
    /// 右键菜单管理器 - 使用 AddMenuPopupItem4 为 FeatureManager 设计树中的实体添加右键菜单
    /// </summary>
  public partial class AddinStudy 
{

              
      
      public string new_drawing_from_part()
      {
          return ExecuteContextMenuCommand("new_drawing_from_part");
      }

      public string new_drw_cnc_menu()
      {
          return ExecuteContextMenuCommand("new_drw_cnc_menu");
      }

      [SolidWorksAddinStudy.Command(2005, "新建工程图(新流程)", "为当前零件/装配体创建工程图并添加视图", "new_drw_cnc_menu", (int)swDocumentTypes_e.swDocPART, (int)swDocumentTypes_e.swDocASSEMBLY, Source = CommandSource.ContextMenu)]
      private void NewDrwCncFromContextMenu()
      {
          CreateDrawingFromSelectedPartWithNewFlow();
      }

      /// <summary>
      /// 按当前选中解析目标零件（面/实体/组件），与工具栏 newdrw / new_drw_cnc 区分的是此处必须能落到具体零件文档。
      /// </summary>
      private bool TryGetTargetPartFromContextSelection(out ModelDoc2 targetPart)
      {
          targetPart = null;
          if (swApp == null)
          {
              Debug.WriteLine("SolidWorks 未初始化");
              return false;
          }

          var activeModel = (ModelDoc2)swApp.ActiveDoc;
          if (activeModel == null)
          {
              swApp.SendMsgToUser("请先打开一个文档");
              return false;
          }

          var selectedParts = CollectSelectedPartDocuments(activeModel);
          if (selectedParts.Count == 0)
          {
              swApp.SendMsgToUser("请先在零件或装配体中选中一个对象（面/实体/组件）");
              return false;
          }

          targetPart = selectedParts[0];
          if (string.IsNullOrWhiteSpace(targetPart.GetPathName()))
          {
              swApp.SendMsgToUser("选中的零件尚未保存，请先保存零件");
              targetPart = null;
              return false;
          }

          return true;
      }

      private void CreateDrawingFromSelectedPartWithLegacyFlow()
      {
          try
          {
              if (!TryGetTargetPartFromContextSelection(out var targetPart))
                  return;

              add_name2info.run(targetPart);
              var checkResult = checkk_factor.RunWithStats(swApp, targetPart);
              if (checkResult.ErrorCount > 0)
              {
                  swApp.SendMsgToUser2(
                      $"K因子检查发现 {checkResult.ErrorCount} 个错误，请先处理后再出图。",
                      (int)swMessageBoxIcon_e.swMbWarning,
                      (int)swMessageBoxBtn_e.swMbOk);
              }
              New_drw.run(swApp, targetPart);
          }
          catch (Exception ex)
          {
              Debug.WriteLine($"右键新建工程图(New_drw)失败：{ex.Message}");
              swApp?.SendMsgToUser($"右键新建工程图失败：{ex.Message}");
          }
      }

      private void CreateDrawingFromSelectedPartWithNewFlow()
      {
          try
          {
              if (!TryGetTargetPartFromContextSelection(out var targetPart))
                  return;

              New_drw_cnc.run(swApp, targetPart);
          }
          catch (Exception ex)
          {
              Debug.WriteLine($"右键新建工程图(new_drw_cnc)失败：{ex.Message}");
              swApp?.SendMsgToUser($"右键新建工程图失败：{ex.Message}");
          }
      }

      [SolidWorksAddinStudy.Command(2001, "实体新建工程图", "从当前选中实体创建工程图", "new_drawing_from_part", (int)swDocumentTypes_e.swDocPART, (int)swDocumentTypes_e.swDocASSEMBLY, ShowOutputWindow = true, Source = CommandSource.ContextMenu)]
      private string NewDrawingFromPartCore()
      {
          try
          {
              Debug.WriteLine("开始创建工程图（New_drw）");
              CreateDrawingFromSelectedPartWithLegacyFlow();
              return "工程图已创建";
          }
          catch (Exception ex)
          {
              Debug.WriteLine($"创建工程图失败：{ex.Message}");
              swApp?.SendMsgToUser($"创建工程图失败：{ex.Message}");
              return $"创建工程图失败：{ex.Message}";
          }
      }

      public string export_selected_to_step()
      {
          return ExecuteContextMenuCommand("export_selected_to_step");
      }

      [SolidWorksAddinStudy.Command(2002, "实体导出STEP", "导出当前选中实体为STEP", "export_selected_to_step", (int)swDocumentTypes_e.swDocPART, (int)swDocumentTypes_e.swDocASSEMBLY, ShowOutputWindow = true, Source = CommandSource.ContextMenu)]
      private string ExportSelectedToStepCore()
      {
          try
          {
              Debug.WriteLine("开始导出STEP");
              if (swApp == null)
              {
                  Debug.WriteLine("SolidWorks 未初始化");
                  return "SolidWorks 未初始化";
              }

              var acswModel = (ModelDoc2)swApp.ActiveDoc;
              if (acswModel == null)
              {
                  Debug.WriteLine("没有打开的文档");
                  swApp.SendMsgToUser("请先打开一个文档");
                  return "没有打开的文档";
              }

              if (!TryGetTargetPartFromContextSelection(out var swModel) || swModel == null)
              {
                  Debug.WriteLine("未选中可解析的零件对象");
                  swApp.SendMsgToUser("请先选中一个对象（面/实体/组件）");
                  return "未选中有效对象";
              }

              string fullPath = swModel.GetPathName();
              if (string.IsNullOrEmpty(fullPath))
              {
                  Debug.WriteLine("文档尚未保存");
                  swApp.SendMsgToUser("请先保存文档");
                  return "文档尚未保存";
              }

              string partname = Path.GetFileNameWithoutExtension(fullPath);
              string? currentDirectory = Path.GetDirectoryName(fullPath);
              
              string outputPath = Path.Combine(currentDirectory, "step", $"{partname}.STEP");
              string outputDirectory = Path.GetDirectoryName(outputPath);
              
              if (!Directory.Exists(outputDirectory))
              {
                  Directory.CreateDirectory(outputDirectory);
              }

              // 导出前清空选择集，避免零件文档中“按当前选中面”仅导出局部几何
              swModel.ClearSelection2(true);
              var result = swModel.SaveAs3(outputPath, 0, 2);
              
              Debug.WriteLine($"STEP导出结果: {result}, 路径: {outputPath}");
              swApp.SendMsgToUser($"STEP文件已导出到: {outputPath}");
              return $"STEP文件已导出: {outputPath}";
          }
          catch (Exception ex)
          {
              Debug.WriteLine($"导出STEP失败：{ex.Message}");
              swApp?.SendMsgToUser($"导出STEP失败：{ex.Message}");
              return $"导出STEP失败：{ex.Message}";
          }
      }

      public string check_k_factor()
      {
          return ExecuteContextMenuCommand("check_k_factor");
      }

      public string fix_bends_kcheck_menu()
      {
          return ExecuteContextMenuCommand("fix_bends_kcheck_menu");
      }

      [SolidWorksAddinStudy.Command(2007, "实体修正折弯K", "默认可按板厚折弯扣除(Type4)，并按 K 检查规则修正折弯（按 body）", "fix_bends_kcheck_menu", (int)swDocumentTypes_e.swDocPART, (int)swDocumentTypes_e.swDocASSEMBLY, ShowOutputWindow = true, Source = CommandSource.ContextMenu)]
      private string FixBendsKcheckFromContextMenu()
      {
          try
          {
              if (swApp == null)
              {
                  return "SolidWorks 未初始化";
              }

              if (!TryGetTargetPartFromContextSelection(out var swModel) || swModel == null)
              {
                  Console.WriteLine("请先选中一个对象（面/实体/组件）");
                  return "未选中有效对象";
              }

              if (swModel.GetType() != (int)swDocumentTypes_e.swDocPART)
              {
                  return "请在零件文档中使用";
              }

              var stats = fix_bends_kcheck.Run(swApp, swModel);
              return stats.Errors > 0
                  ? $"修正完成但有 {stats.Errors} 处失败，见输出窗口"
                  : $"修正完成：钣金默认 {stats.SheetMetalFeaturesUpdated}，折弯 {stats.BendsUpdated}";
          }
          catch (Exception ex)
          {
              Console.WriteLine($"fix_bends_kcheck: 失败 — {ex.Message}");
              return ex.Message;
          }
      }

      [SolidWorksAddinStudy.Command(2003, "实体检查K因子", "检查当前选中实体的K因子", "check_k_factor", (int)swDocumentTypes_e.swDocPART, (int)swDocumentTypes_e.swDocASSEMBLY, ShowOutputWindow = true, Source = CommandSource.ContextMenu)]
      private string CheckKFactorCore()
      {
          try
          {
              Debug.WriteLine("开始检查K因子");
              if (swApp == null)
              {
                  Debug.WriteLine("SolidWorks 未初始化");
                  return "SolidWorks 未初始化";
              }

              var acswModel = (ModelDoc2)swApp.ActiveDoc;
              if (acswModel == null)
              {
                  Debug.WriteLine("没有打开的文档");
                  Console.WriteLine("请先打开一个零件文档");
                  return "没有打开的文档";
              }

              if (!TryGetTargetPartFromContextSelection(out var swModel) || swModel == null)
              {
                  Debug.WriteLine("未选中可解析的零件对象");
                  Console.WriteLine("请先选中一个对象（面/实体/组件）");
                  return "未选中有效对象";
              }

              // 检查是否为零件文档
              if (swModel.GetType() != (int)swDocumentTypes_e.swDocPART)
              {
                  Debug.WriteLine("当前文档不是零件");
                  Console.WriteLine("请在零件文档中使用此功能");
                  return "请在零件文档中使用此功能";
              }

              checkk_factor.run(swApp, swModel);
              
              Debug.WriteLine("K因子检查完成");
            
              return "K因子检查完成";
          }
          catch (Exception ex)
          {
              Debug.WriteLine($"K因子检查失败：{ex.Message}");
              swApp?.SendMsgToUser($"K因子检查失败：{ex.Message}");
              return $"K因子检查失败：{ex.Message}";
          }
      }

      public string modify_equations()
      {
          return ExecuteContextMenuCommand("modify_equations");
      }

      public string mark_square_tube()
      {
          return ExecuteContextMenuCommand("mark_square_tube");
      }

      [SolidWorksAddinStudy.Command(2006, "标注为方管", "将当前选中对象对应零件标注为方管（用于拓扑识别）", "mark_square_tube", (int)swDocumentTypes_e.swDocPART, (int)swDocumentTypes_e.swDocASSEMBLY, ShowOutputWindow = true, Source = CommandSource.ContextMenu)]
      private string MarkSquareTubeCore()
      {
          try
          {
              if (swApp == null)
              {
                  Debug.WriteLine("SolidWorks 未初始化");
                  return "SolidWorks 未初始化";
              }

              ModelDoc2 activeModel = (ModelDoc2)swApp.ActiveDoc;
              if (activeModel == null)
              {
                  swApp.SendMsgToUser("请先打开一个文档");
                  return "没有打开的文档";
              }

              if (!TryGetTargetPartFromContextSelection(out var targetPart) || targetPart == null)
              {
                  swApp.SendMsgToUser("请先选中一个对象（面/实体/组件）");
                  return "未选中有效对象";
              }

              if (targetPart.GetType() != (int)swDocumentTypes_e.swDocPART)
              {
                  swApp.SendMsgToUser("请选择零件对象");
                  return "目标不是零件文档";
              }

              string partPath = targetPart.GetPathName();
              if (string.IsNullOrWhiteSpace(partPath))
              {
                  swApp.SendMsgToUser("选中的零件尚未保存，请先保存零件");
                  return "零件未保存";
              }

              var graphs = FaceGraphBuilder.BuildGraphs(targetPart);
              if (graphs == null || graphs.Count == 0)
              {
                  swApp.SendMsgToUser("无法构建零件拓扑图，标注失败");
                  return "构建拓扑图失败";
              }

              TopologyLabeler.Initialize();
              TopologyDatabase? db = TopologyLabeler.GetDatabase();
              if (db == null)
              {
                  swApp.SendMsgToUser("拓扑数据库未初始化，标注失败");
                  return "拓扑数据库未初始化";
              }

              string partName = targetPart.GetTitle();
              List<int> bodyIds = db.UpsertPartWithBodies(partName, partPath, graphs);
              int labeledCount = 0;
              foreach (int bodyId in bodyIds)
              {
                  db.AddLabel(bodyId, "零件类别", "方管", 1.0, "右键手工标注");
                  labeledCount++;
              }

              string msg = $"已标注方管: {Path.GetFileNameWithoutExtension(partPath)}，Body 数={labeledCount}";
              Debug.WriteLine(msg);
              swApp.SendMsgToUser(msg);
              return msg;
          }
          catch (Exception ex)
          {
              Debug.WriteLine($"右键标注方管失败：{ex.Message}");
              swApp?.SendMsgToUser($"右键标注方管失败：{ex.Message}");
              return $"右键标注方管失败：{ex.Message}";
          }
      }

      [SolidWorksAddinStudy.Command(2004, "实体修改方程式", "修改当前选中对象相关零件的方程式", "modify_equations", (int)swDocumentTypes_e.swDocPART, (int)swDocumentTypes_e.swDocASSEMBLY, ShowOutputWindow = true, Source = CommandSource.ContextMenu)]
      private string ModifyEquationsCore()
      {
          try
          {
              Debug.WriteLine("开始修改方程式");
              if (swApp == null)
              {
                  Debug.WriteLine("SolidWorks 未初始化");
                  return "SolidWorks 未初始化";
              }

              var acswModel = (ModelDoc2)swApp.ActiveDoc;
              if (acswModel == null)
              {
                  Debug.WriteLine("没有打开的文档");
                  swApp.SendMsgToUser("请先打开一个文档");
                  return "没有打开的文档";
              }

              var parts = CollectSelectedPartDocuments(acswModel);
              if (parts.Count == 0)
              {
                  Debug.WriteLine("未选中有效对象或无法解析为零件");
                  swApp.SendMsgToUser("请在零件或装配体中选中一个或多个对象（面/实体/组件等，装配体中需能解析到零件文档）");
                  return "未选中实体";
              }

              Debug.WriteLine($"目标零件数: {parts.Count}");
              return EquationModifier.ModifyEquationsForParts(swApp, parts);
          }
          catch (Exception ex)
          {
              Debug.WriteLine($"修改方程式失败：{ex.Message}");
              swApp?.SendMsgToUser($"修改方程式失败：{ex.Message}");
              return $"修改方程式失败：{ex.Message}";
          }
      }

      private string ExecuteContextMenuCommand(string commandName)
      {
          try
          {
              bool ok = ExecuteCommandByName(commandName, CommandSource.ContextMenu);
              if (!ok)
              {
                  string msg = $"未找到右键命令: {commandName}";
                  Debug.WriteLine(msg);
                  swApp?.SendMsgToUser(msg);
                  return msg;
              }
              return "ok";
          }
          catch (Exception ex)
          {
              string msg = $"右键命令执行失败: {commandName}, {ex.Message}";
              Debug.WriteLine(msg);
              swApp?.SendMsgToUser(msg);
              return msg;
          }
      }

      /// <summary>
          /// 从当前多选对象中收集不重复的零件文档（支持面、实体、组件等）。
      /// </summary>
      private static List<ModelDoc2> CollectSelectedPartDocuments(ModelDoc2 activeDoc)
      {
          var result = new List<ModelDoc2>();
          var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
          var swApp = AddinStudy.GetSwApp();

          void TryAdd(ModelDoc2? partDoc)
          {
              if (partDoc == null || partDoc.GetType() != (int)swDocumentTypes_e.swDocPART)
              {
                  return;
              }

              string key = partDoc.GetPathName();
              if (string.IsNullOrEmpty(key))
              {
                  key = partDoc.GetTitle();
              }

              if (seen.Add(key))
              {
                  result.Add(partDoc);
              }
          }

          ModelDoc2? EnsurePartDocVisible(Component2 comp)
          {
              if (comp == null)
              {
                  return null;
              }

              ModelDoc2? partDoc = comp.GetModelDoc2() as ModelDoc2;
              if (partDoc == null)
              {
                  string path = comp.GetPathName();
                  if (!string.IsNullOrEmpty(path) && swApp != null)
                  {
                      try
                      {
                          int errors = 0;
                          int warnings = 0;
                          partDoc = swApp.OpenDoc6(
                              path,
                              (int)swDocumentTypes_e.swDocPART,
                              (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                              "",
                              ref errors,
                              ref warnings) as ModelDoc2;
                      }
                      catch (Exception ex)
                      {
                          Debug.WriteLine($"打开零件文档失败: {path}, {ex.Message}");
                      }
                  }
              }

              if (partDoc == null)
              {
                  return null;
              }

              try
              {
                  partDoc.Visible = true;
              }
              catch (Exception ex)
              {
                  Debug.WriteLine($"设置零件可见失败: {partDoc.GetTitle()}, {ex.Message}");
              }

              return partDoc.Visible ? partDoc : null;
          }

          var swSelMgr = (SelectionMgr)activeDoc.SelectionManager;
          int n = swSelMgr.GetSelectedObjectCount2(-1);
          if (n <= 0)
          {
              return result;
          }

          if (activeDoc.GetType() == (int)swDocumentTypes_e.swDocPART)
          {
              bool anySelection = false;
              for (int i = 1; i <= n; i++)
              {
                  object selObj = swSelMgr.GetSelectedObject6(i, -1);
                  if (selObj != null)
                  {
                      anySelection = true;
                      break;
                  }
              }

              if (anySelection)
              {
                  TryAdd(activeDoc);
              }

              return result;
          }

          if (activeDoc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
          {
              for (int i = 1; i <= n; i++)
              {
                  object selObj = swSelMgr.GetSelectedObject6(i, -1);
                  var comp = (Component2)swSelMgr.GetSelectedObjectsComponent3(i, -1);
                  if (comp == null && selObj is Component2 directComp)
                  {
                      comp = directComp;
                  }

                  // 优先按组件解析（支持装配体树/组件选择，不限于面）
                  if (comp != null)
                  {
                      var partModelFromComp = EnsurePartDocVisible(comp);
                      TryAdd(partModelFromComp);
                      continue;
                  }

                  IBody2 body = null;
                  if (selObj is Face2 face)
                  {
                      body = (IBody2)face.GetBody();
                  }
                  else if (selObj is IBody2 b)
                  {
                      body = b;
                  }

                  if (body == null)
                  {
                      continue;
                  }

                  comp = (Component2)swSelMgr.GetSelectedObjectsComponent3(i, -1);
                  var partModel = comp == null ? null : EnsurePartDocVisible(comp);
                  TryAdd(partModel);
              }
          }

          return result;
      }

       public (ModelDoc2,IBody2) get_select_body(ModelDoc2 swModel)
       {
           try
           {
               var swSelMgr = (SelectionMgr)swModel.SelectionManager;
               
               // 检查是否有选中对象
               if (swSelMgr.GetSelectedObjectCount2(-1) <= 0)
               {
                   Debug.WriteLine("没有选中任何对象");
                   return (null, null);
               }
               
               var selObj = swSelMgr.GetSelectedObject6(1, -1);
               if (selObj == null)
               {
                   Debug.WriteLine("无法获取选中对象");
                   return (null, null);
               }
               
               Face2 face = null;
               IBody2 body = null;
               
               // 尝试从选中的对象获取面和实体
               if (selObj is Face2)
               {
                   face = (Face2)selObj;
                   body = (IBody2)face.GetBody();
               }
               else if (selObj is IBody2)
               {
                   body = (IBody2)selObj;
               }
               else
               {
                   Debug.WriteLine($"选中的对象类型不支持: {selObj.GetType().Name}");
                   return (null, null);
               }
               
               if (body == null)
               {
                   Debug.WriteLine("无法获取实体对象");
                   return (null, null);
               }
               
               // 如果是装配体，需要获取零件文档
               if (swModel.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
               {
                   var comp = (Component2)swSelMgr.GetSelectedObjectsComponent3(1, -1);
                   if (comp != null)
                   {
                       var partModel = (ModelDoc2)comp.GetModelDoc2();
                       if (partModel != null)
                       {
                           Console.WriteLine($"从装配体获取零件文档: {partModel.GetTitle()}");
                           return (partModel, body);
                       }
                   }
                   Console.WriteLine("无法从装配体获取零件文档");
                   return (null, null);
               }

               Console.WriteLine(body.Name);
               return (swModel, body);
           }
           catch (Exception ex)
           {
               Debug.WriteLine($"get_select_body 错误: {ex.Message}");
               return (null, null);
           }
       }

   


        /// <summary>
        /// 初始化实体右键菜单
        /// </summary>
        public void PopupMenuInitialize()
        {
            try
            {
                if (swApp == null || iCmdMgr == null)
                {
                    Debug.WriteLine("SolidWorks 未初始化");
                    return;
                }

                RegisterContextMenuForCommonSelections("new_drw", "new_drawing_from_part");
                RegisterContextMenuForCommonSelections("new_drw_cnc", "new_drw_cnc_menu");
                RegisterContextMenuForCommonSelections("export_step", "export_selected_to_step");
                RegisterContextMenuForCommonSelections("check_k_factor", "check_k_factor");
                RegisterContextMenuForCommonSelections("修正折弯K", "fix_bends_kcheck_menu");
                RegisterContextMenuForCommonSelections("modify_equations", "modify_equations");
                RegisterContextMenuForCommonSelections("标注当前零件为方管", "mark_square_tube");

                      
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[实体右键菜单] 初始化失败：{ex.Message}");
            }
        }

        private void RegisterContextMenuForCommonSelections(string menuText, string callbackName)
        {
            int[] docTypes =
            {
                (int)swDocumentTypes_e.swDocPART,
                (int)swDocumentTypes_e.swDocASSEMBLY
            };

            int[] selectTypes =
            {
                (int)swSelectType_e.swSelFACES,
                (int)swSelectType_e.swSelSOLIDBODIES,
                (int)swSelectType_e.swSelCOMPONENTS
            };

            foreach (int docType in docTypes)
            {
                foreach (int selectType in selectTypes)
                {
                    swApp.AddMenuPopupItem2(docType, addinCookieID, selectType, menuText, callbackName, "", "", "");
                }
            }
        }

      

   

       
    }
}