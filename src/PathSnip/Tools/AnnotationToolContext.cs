using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PathSnip.Tools
{
    /// <summary>
    /// 标注工具上下文环境
    /// 封装绘图所需的画布、样式、边界等信息
    /// </summary>
    public class AnnotationToolContext
    {
        /// <summary>
        /// 标注画布（用于矩形、箭头、文字、步骤序号等）
        /// </summary>
        public Canvas AnnotationCanvas { get; set; }

        /// <summary>
        /// 马赛克画布（用于马赛克轨迹）
        /// </summary>
        public Canvas MosaicCanvas { get; set; }

        /// <summary>
        /// 当前选中的颜色
        /// </summary>
        public Color CurrentColor { get; set; }

        /// <summary>
        /// 当前选中的颜色（画刷）
        /// </summary>
        public Brush CurrentColorBrush { get; set; }

        /// <summary>
        /// 当前选中的粗细
        /// </summary>
        public double CurrentThickness { get; set; }

        /// <summary>
        /// 当前截图选区的边界（用于限制绘制不能超出选区）
        /// </summary>
        public Rect SelectionBounds { get; set; }

        /// <summary>
        /// 步骤序号计数器（如需使用）
        /// </summary>
        public int StepCounter { get; set; } = 1;

        /// <summary>
        /// 马赛克块大小（用于创建马赛克笔刷）
        /// </summary>
        public int MosaicBlockSize { get; set; } = 16;

        /// <summary>
        /// 预渲染的马赛克背景（用于创建马赛克笔刷）
        /// </summary>
        public BitmapSource MosaicBackground { get; set; }

        /// <summary>
        /// 撤销栈
        /// </summary>
        public Stack<UndoAction> UndoStack { get; } = new Stack<UndoAction>();

        /// <summary>
        /// 重做栈
        /// </summary>
        public Stack<UndoAction> RedoStack { get; } = new Stack<UndoAction>();

        /// <summary>
        /// 最大撤销次数
        /// </summary>
        public const int MaxUndoCount = 50;

        /// <summary>
        /// 将撤销/重做操作推入栈
        /// </summary>
        public void PushToUndo(UndoAction action)
        {
            UndoStack.Push(action);
            RedoStack.Clear();

            // 容量限制：当超出最大次数时
            if (UndoStack.Count > MaxUndoCount)
            {
                // Stack.ToArray() 出来的数组，[0]是最新操作，末尾是最老的操作
                var items = UndoStack.ToArray();
                UndoStack.Clear();
                
                // 丢弃最老的一个（items.Length - 1），把剩下的按从老到新的顺序重新压栈
                for (int i = MaxUndoCount - 1; i >= 0; i--)
                {
                    UndoStack.Push(items[i]);
                }
            }
        }

        /// <summary>
        /// 执行撤销
        /// </summary>
        public void Undo()
        {
            if (UndoStack.Count > 0)
            {
                var action = UndoStack.Pop();
                action.Undo();
                RedoStack.Push(action);
            }
        }

        /// <summary>
        /// 执行重做
        /// </summary>
        public void Redo()
        {
            if (RedoStack.Count > 0)
            {
                var action = RedoStack.Pop();
                action.Redo();
                UndoStack.Push(action);
            }
        }

        /// <summary>
        /// 将点限制在选区边界内
        /// </summary>
        public Point ClampToBounds(Point point)
        {
            var x = Math.Max(SelectionBounds.Left, Math.Min(SelectionBounds.Right, point.X));
            var y = Math.Max(SelectionBounds.Top, Math.Min(SelectionBounds.Bottom, point.Y));
            return new Point(x, y);
        }

        /// <summary>
        /// 将矩形限制在选区边界内
        /// </summary>
        public Rect ClampRectToBounds(Rect rect)
        {
            var x = Math.Max(SelectionBounds.Left, rect.X);
            var y = Math.Max(SelectionBounds.Top, rect.Y);
            var right = Math.Min(SelectionBounds.Right, rect.Right);
            var bottom = Math.Min(SelectionBounds.Bottom, rect.Bottom);

            return new Rect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
        }

        /// <summary>
        /// 创建马赛克笔刷
        /// </summary>
        public Brush CreateMosaicBrush()
        {
            // 如果预渲染失败，做个保底
            if (MosaicBackground == null)
            {
                return Brushes.Gray;
            }

            // 使用 ImageBrush，把全屏的马赛克底图作为画笔的"颜料"
            return new ImageBrush(MosaicBackground)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                Viewbox = new Rect(0, 0, MosaicBackground.Width, MosaicBackground.Height),
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, MosaicBackground.Width, MosaicBackground.Height),
                ViewportUnits = BrushMappingMode.Absolute
            };
        }

        /// <summary>
        /// 更新当前样式（颜色、粗细等）
        /// </summary>
        public void UpdateStyle(Color color, double thickness, int mosaicBlockSize)
        {
            CurrentColor = color;
            CurrentColorBrush = new SolidColorBrush(color);
            CurrentThickness = thickness;
            MosaicBlockSize = mosaicBlockSize;
        }
    }
}
