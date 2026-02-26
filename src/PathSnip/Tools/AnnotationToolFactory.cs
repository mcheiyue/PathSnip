using System;

namespace PathSnip.Tools
{
    /// <summary>
    /// 标注工具工厂
    /// </summary>
    public static class AnnotationToolFactory
    {
        public static IAnnotationTool Create(AnnotationType type)
        {
            return type switch
            {
                AnnotationType.Rectangle => new RectangleTool(),
                // 其他工具待迁移
                _ => throw new ArgumentException($"Unknown annotation tool type: {type}")
            };
        }
    }
}
