using System;

namespace SolidWorksAddinStudy
{
    /// <summary>
    /// 零件状态信息类
    /// </summary>
    public class PartStatusInfo
    {
        /// <summary>
        /// 零件名称
        /// </summary>
        public string PartName { get; set; }
        
        /// <summary>
        /// 零件类型
        /// </summary>
        public string PartType { get; set; }
        
        /// <summary>
        /// 规格尺寸
        /// </summary>
        public string Dimension { get; set; }
        
        /// <summary>
        /// 是否出图
        /// </summary>
        public string IsDrawn { get; set; }
        
        /// <summary>
        /// 数量
        /// </summary>
        public string Quantity { get; set; }
        
        /// <summary>
        /// 构造函数
        /// </summary>
        public PartStatusInfo()
        {
            PartName = string.Empty;
            PartType = string.Empty;
            Dimension = string.Empty;
            IsDrawn = "未出图"; // 默认值为未出图
            Quantity = string.Empty;
        }
        
        /// <summary>
        /// 带参数的构造函数
        /// </summary>
        public PartStatusInfo(string partName, string partType, string dimension, string isDrawn, string quantity)
        {
            PartName = partName;
            PartType = partType;
            Dimension = dimension;
            IsDrawn = isDrawn;
            Quantity = quantity;
        }
    }
}