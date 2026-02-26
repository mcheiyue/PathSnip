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
            switch (type)
            {
                case AnnotationType.Rectangle:
                    return new RectangleTool();
                case AnnotationType.Arrow:
                    return new ArrowTool();
                case AnnotationType.Text:
                    return new TextTool();
                case AnnotationType.Mosaic:
                    return new MosaicTool();
                case AnnotationType.StepMarker:
                    return new StepMarkerTool();
                default:
                    throw new ArgumentException($"Unknown annotation tool type: {type}");
            }
        }
    }
}
