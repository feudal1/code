/** 与插件 WorkProjectTaskPaneControl / XmlSerializer 字段对齐 */
export interface WorkProjectItem {
  ProjectName: string;
  ProjectAssemblyPath: string;
  RollerDrawing: string;
  RollerDrawingUpdatedAt: string;
  RollerFollowUp: string;
  PipeDrawing: string;
  PipeDrawingUpdatedAt: string;
  PipeFollowUp: string;
  SheetMetalDrawing: string;
  SheetMetalDrawingUpdatedAt: string;
  SheetMetalFollowUp: string;
  MachiningDrawing: string;
  MachiningDrawingUpdatedAt: string;
  MachiningFollowUp: string;
  PurchasedProcurement: string;
  PurchasedProcurementUpdatedAt: string;
  PurchasedFollowUp: string;
  BearingProcurement: string;
  BearingProcurementUpdatedAt: string;
  BearingFollowUp: string;
  TimingBeltProcurement: string;
  TimingBeltProcurementUpdatedAt: string;
  TimingBeltFollowUp: string;
  CylinderSelection: string;
  CylinderSelectionUpdatedAt: string;
  CylinderSelectionFollowUp: string;
  CountersinkMarkingDrawing: string;
  CountersinkMarkingDrawingUpdatedAt: string;
  CountersinkMarkingFollowUp: string;
}

export const emptyProjectItem = (name: string): WorkProjectItem => ({
  ProjectName: name,
  ProjectAssemblyPath: "",
  RollerDrawing: "",
  RollerDrawingUpdatedAt: minDateTimeXml(),
  RollerFollowUp: "",
  PipeDrawing: "",
  PipeDrawingUpdatedAt: minDateTimeXml(),
  PipeFollowUp: "",
  SheetMetalDrawing: "",
  SheetMetalDrawingUpdatedAt: minDateTimeXml(),
  SheetMetalFollowUp: "",
  MachiningDrawing: "",
  MachiningDrawingUpdatedAt: minDateTimeXml(),
  MachiningFollowUp: "",
  PurchasedProcurement: "",
  PurchasedProcurementUpdatedAt: minDateTimeXml(),
  PurchasedFollowUp: "",
  BearingProcurement: "",
  BearingProcurementUpdatedAt: minDateTimeXml(),
  BearingFollowUp: "",
  TimingBeltProcurement: "",
  TimingBeltProcurementUpdatedAt: minDateTimeXml(),
  TimingBeltFollowUp: "",
  CylinderSelection: "",
  CylinderSelectionUpdatedAt: minDateTimeXml(),
  CylinderSelectionFollowUp: "",
  CountersinkMarkingDrawing: "",
  CountersinkMarkingDrawingUpdatedAt: minDateTimeXml(),
  CountersinkMarkingFollowUp: "",
});

/** 与 .NET DateTime.MinValue / XmlSerializer 常见序列化一致 */
export function minDateTimeXml(): string {
  return "0001-01-01T00:00:00";
}

export type CategoryKey =
  | "roller"
  | "pipe"
  | "sheetMetal"
  | "machining"
  | "purchase"
  | "bearing"
  | "timingBelt"
  | "cylinder"
  | "countersink";

export interface CategoryDef {
  key: CategoryKey;
  title: string;
  pathFields: PathFieldPair;
  /** 与 C# ShouldPickFilePath 一致：true=文件框，false=文件夹语义（此处均为文本，仅影响提示） */
  pathIsFile: boolean;
}

interface PathFieldPair {
  path: keyof WorkProjectItem;
  followUp: keyof WorkProjectItem;
  updatedAt: keyof WorkProjectItem;
}

export const CATEGORIES: CategoryDef[] = [
  {
    key: "roller",
    title: "滚筒出图",
    pathFields: {
      path: "RollerDrawing",
      followUp: "RollerFollowUp",
      updatedAt: "RollerDrawingUpdatedAt",
    },
    pathIsFile: true,
  },
  {
    key: "pipe",
    title: "管件出图",
    pathFields: {
      path: "PipeDrawing",
      followUp: "PipeFollowUp",
      updatedAt: "PipeDrawingUpdatedAt",
    },
    pathIsFile: false,
  },
  {
    key: "sheetMetal",
    title: "钣金出图",
    pathFields: {
      path: "SheetMetalDrawing",
      followUp: "SheetMetalFollowUp",
      updatedAt: "SheetMetalDrawingUpdatedAt",
    },
    pathIsFile: false,
  },
  {
    key: "machining",
    title: "机加出图",
    pathFields: {
      path: "MachiningDrawing",
      followUp: "MachiningFollowUp",
      updatedAt: "MachiningDrawingUpdatedAt",
    },
    pathIsFile: false,
  },
  {
    key: "purchase",
    title: "外购采购",
    pathFields: {
      path: "PurchasedProcurement",
      followUp: "PurchasedFollowUp",
      updatedAt: "PurchasedProcurementUpdatedAt",
    },
    pathIsFile: true,
  },
  {
    key: "bearing",
    title: "轴承采购",
    pathFields: {
      path: "BearingProcurement",
      followUp: "BearingFollowUp",
      updatedAt: "BearingProcurementUpdatedAt",
    },
    pathIsFile: true,
  },
  {
    key: "timingBelt",
    title: "同步带采购",
    pathFields: {
      path: "TimingBeltProcurement",
      followUp: "TimingBeltFollowUp",
      updatedAt: "TimingBeltProcurementUpdatedAt",
    },
    pathIsFile: true,
  },
  {
    key: "cylinder",
    title: "气缸选型",
    pathFields: {
      path: "CylinderSelection",
      followUp: "CylinderSelectionFollowUp",
      updatedAt: "CylinderSelectionUpdatedAt",
    },
    pathIsFile: true,
  },
  {
    key: "countersink",
    title: "打标沉孔",
    pathFields: {
      path: "CountersinkMarkingDrawing",
      followUp: "CountersinkMarkingFollowUp",
      updatedAt: "CountersinkMarkingDrawingUpdatedAt",
    },
    pathIsFile: false,
  },
];
