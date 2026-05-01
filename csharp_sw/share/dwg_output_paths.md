# DWG/DXF 输出路径总表

这个文件用于补充 `dwg_output_paths.json`（JSON 本身不能写注释）。

## 配置文件

- 文件：`dwg_output_paths.json`
- 读取类：`src/nomal/dwg_output_paths.cs`

## 字段说明（含“是谁在用”）

- `OutputRootTemplate`
  - 含义：输出根目录模板，当前固定输出到 `钣金` 根目录（不再拼接当前文件夹名）。
  - 默认：`钣金`
  - 使用方：
    - `src/drw/drw2dwg.cs`（工程图转 DWG）
    - `src/drw/drw2dxf.cs`（工程图转 DXF）
    - `src/drw/opendwg.cs`（打开或补导 DWG）
    - `src/part/open_select_dwg.cs`（按选中实体打开 DWG）
    - `src/part/exportdwg2_body.cs`（按实体导出 DWG）

- `FallbackRootName`
  - 含义：模板为空或目录名不可用时的兜底根目录名。
  - 默认：`钣金`
  - 使用方：同上（由 `DwgOutputPaths.ResolveOutputRoot` 统一兜底）。

- `LegacyTestRootFolder`
  - 含义：旧导出流程专用测试根目录名。
  - 默认：`测试`
  - 使用方：
    - `src/part/exportdwg2.cs`（历史导出入口，输出到“测试”目录）

- `WeldmentFolder`
  - 含义：焊接图目录名。
  - 默认：`焊接图`
  - 使用方：
    - `src/drw/drw2dwg.cs`
    - `src/drw/opendwg.cs`

- `CncFolder`
  - 含义：CNC 目录名（厚度为 0 时）。
  - 默认：`CNC`
  - 使用方：
    - `src/drw/drw2dwg.cs`
    - `src/drw/opendwg.cs`

- `EngineeringFolder`
  - 含义：工程图目录名。
  - 默认：`工程图`
  - 使用方：
    - `src/drw/drw2dwg.cs`
    - `src/drw/drw2dxf.cs`
    - `src/drw/opendwg.cs`

- `SheetMetalFolder`
  - 含义：下料目录名。
  - 默认：`下料`
  - 使用方：
    - `src/part/open_select_dwg.cs`
    - `src/part/exportdwg2_body.cs`
    - `src/part/exportdwg2.cs`

- `SusThicknessPrefix`
  - 含义：不锈钢厚度目录前缀。
  - 默认：`sus`
  - 使用方：
    - `src/drw/drw2dwg.cs`
    - `src/drw/opendwg.cs`
    - `src/nomal/dwg_output_paths.cs`（统一材质判定）

## 典型输出路径（当前规则）

- 工程图 DWG：`<零件目录>\<输出根目录>\<工程图>\<材质厚度>\xxx.dwg`
- 工程图 DXF：`<零件目录>\<输出根目录>\<工程图>\xxx.dxf`
- 焊接图 DWG：`<零件目录>\<输出根目录>\<焊接图>\xxx.dwg`
- CNC DWG：`<零件目录>\<输出根目录>\<CNC>\xxx.dwg`
- 下料 DWG（实体导出）：`<零件目录>\<输出根目录>\<下料>\<材质厚度>\xxx_body.dwg`
- 历史测试导出：`<零件目录>\<测试>\<下料>\<厚度>\xxx.dwg`

## 修改建议

- 只改目录名称：直接改 `dwg_output_paths.json`
- 需新增新分类目录：先在 JSON 增字段，再在 `dwg_output_paths.cs` 补默认值，然后对应业务文件引用该字段
