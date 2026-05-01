import { XMLParser, XMLBuilder } from "fast-xml-parser";
import type { WorkProjectItem } from "./types";
import { emptyProjectItem, minDateTimeXml } from "./types";

const XML_HEADER = '<?xml version="1.0" encoding="utf-8"?>';
const NS_XSI = "http://www.w3.org/2001/XMLSchema-instance";
const NS_XSD = "http://www.w3.org/2001/XMLSchema";

const parser = new XMLParser({
  ignoreAttributes: false,
  trimValues: true,
  parseTagValue: false,
});

const builder = new XMLBuilder({
  format: true,
  ignoreAttributes: false,
  suppressEmptyNode: false,
});

function normalizeItems(raw: unknown): Record<string, unknown>[] {
  if (raw == null) return [];
  if (Array.isArray(raw)) return raw as Record<string, unknown>[];
  return [raw as Record<string, unknown>];
}

function pickStr(row: Record<string, unknown>, ...keys: string[]): string {
  for (const k of keys) {
    const v = row[k];
    if (v !== undefined && v !== null && String(v).length > 0) return String(v);
  }
  return "";
}

function pickIso(row: Record<string, unknown>, key: string): string {
  const v = row[key];
  if (v === undefined || v === null || v === "") return minDateTimeXml();
  const s = String(v);
  if (/^\d{4}-\d{2}-\d{2}/.test(s)) return s;
  return s;
}

function deserializeWorkProjectItem(row: Record<string, unknown>): WorkProjectItem | null {
  const name = pickStr(row, "ProjectName");
  if (!name) return null;
  const assembly =
    pickStr(row, "ProjectAssemblyPath") || pickStr(row, "FolderPath");
  const base = emptyProjectItem(name);
  return {
    ...base,
    ProjectAssemblyPath: assembly,
    RollerDrawing: pickStr(row, "RollerDrawing"),
    RollerDrawingUpdatedAt: pickIso(row, "RollerDrawingUpdatedAt"),
    RollerFollowUp: pickStr(row, "RollerFollowUp"),
    PipeDrawing: pickStr(row, "PipeDrawing"),
    PipeDrawingUpdatedAt: pickIso(row, "PipeDrawingUpdatedAt"),
    PipeFollowUp: pickStr(row, "PipeFollowUp"),
    SheetMetalDrawing: pickStr(row, "SheetMetalDrawing"),
    SheetMetalDrawingUpdatedAt: pickIso(row, "SheetMetalDrawingUpdatedAt"),
    SheetMetalFollowUp: pickStr(row, "SheetMetalFollowUp"),
    MachiningDrawing: pickStr(row, "MachiningDrawing"),
    MachiningDrawingUpdatedAt: pickIso(row, "MachiningDrawingUpdatedAt"),
    MachiningFollowUp: pickStr(row, "MachiningFollowUp"),
    PurchasedProcurement: pickStr(row, "PurchasedProcurement"),
    PurchasedProcurementUpdatedAt: pickIso(row, "PurchasedProcurementUpdatedAt"),
    PurchasedFollowUp: pickStr(row, "PurchasedFollowUp"),
    BearingProcurement: pickStr(row, "BearingProcurement"),
    BearingProcurementUpdatedAt: pickIso(row, "BearingProcurementUpdatedAt"),
    BearingFollowUp: pickStr(row, "BearingFollowUp"),
    TimingBeltProcurement: pickStr(row, "TimingBeltProcurement"),
    TimingBeltProcurementUpdatedAt: pickIso(row, "TimingBeltProcurementUpdatedAt"),
    TimingBeltFollowUp: pickStr(row, "TimingBeltFollowUp"),
    CylinderSelection: pickStr(row, "CylinderSelection"),
    CylinderSelectionUpdatedAt: pickIso(row, "CylinderSelectionUpdatedAt"),
    CylinderSelectionFollowUp: pickStr(row, "CylinderSelectionFollowUp"),
    CountersinkMarkingDrawing: pickStr(row, "CountersinkMarkingDrawing"),
    CountersinkMarkingDrawingUpdatedAt: pickIso(row, "CountersinkMarkingDrawingUpdatedAt"),
    CountersinkMarkingFollowUp: pickStr(row, "CountersinkMarkingFollowUp"),
  };
}

export function parseWorkProjectsXml(xmlText: string): WorkProjectItem[] {
  const doc = parser.parse(xmlText) as Record<string, unknown>;
  const root = doc.WorkProjectStore as Record<string, unknown> | undefined;
  if (!root) throw new Error("根节点不是 WorkProjectStore");
  const projects = root.Projects as Record<string, unknown> | undefined;
  if (!projects) return [];
  const items = normalizeItems(projects.WorkProjectItem);
  const out: WorkProjectItem[] = [];
  for (const row of items) {
    const item = deserializeWorkProjectItem(row);
    if (item) out.push(item);
  }
  return out;
}

function serializeItem(item: WorkProjectItem): Record<string, string> {
  const row: Record<string, string> = {
    ProjectName: item.ProjectName,
    ProjectAssemblyPath: item.ProjectAssemblyPath,
    FolderPath: item.ProjectAssemblyPath,
    RollerDrawing: item.RollerDrawing,
    RollerDrawingUpdatedAt: item.RollerDrawingUpdatedAt,
    RollerFollowUp: item.RollerFollowUp,
    PipeDrawing: item.PipeDrawing,
    PipeDrawingUpdatedAt: item.PipeDrawingUpdatedAt,
    PipeFollowUp: item.PipeFollowUp,
    SheetMetalDrawing: item.SheetMetalDrawing,
    SheetMetalDrawingUpdatedAt: item.SheetMetalDrawingUpdatedAt,
    SheetMetalFollowUp: item.SheetMetalFollowUp,
    MachiningDrawing: item.MachiningDrawing,
    MachiningDrawingUpdatedAt: item.MachiningDrawingUpdatedAt,
    MachiningFollowUp: item.MachiningFollowUp,
    PurchasedProcurement: item.PurchasedProcurement,
    PurchasedProcurementUpdatedAt: item.PurchasedProcurementUpdatedAt,
    PurchasedFollowUp: item.PurchasedFollowUp,
    BearingProcurement: item.BearingProcurement,
    BearingProcurementUpdatedAt: item.BearingProcurementUpdatedAt,
    BearingFollowUp: item.BearingFollowUp,
    TimingBeltProcurement: item.TimingBeltProcurement,
    TimingBeltProcurementUpdatedAt: item.TimingBeltProcurementUpdatedAt,
    TimingBeltFollowUp: item.TimingBeltFollowUp,
    CylinderSelection: item.CylinderSelection,
    CylinderSelectionUpdatedAt: item.CylinderSelectionUpdatedAt,
    CylinderSelectionFollowUp: item.CylinderSelectionFollowUp,
    CountersinkMarkingDrawing: item.CountersinkMarkingDrawing,
    CountersinkMarkingDrawingUpdatedAt: item.CountersinkMarkingDrawingUpdatedAt,
    CountersinkMarkingFollowUp: item.CountersinkMarkingFollowUp,
  };
  return row;
}

export function serializeWorkProjectsXml(items: WorkProjectItem[]): string {
  const payload = {
    WorkProjectStore: {
      "@_xmlns:xsi": NS_XSI,
      "@_xmlns:xsd": NS_XSD,
      Projects: {
        WorkProjectItem: items.map(serializeItem),
      },
    },
  };
  const inner = builder.build(payload);
  if (typeof inner !== "string") throw new Error("XML 构建失败");
  return `${XML_HEADER}\n${inner}`;
}
