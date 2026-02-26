using System;
using System.Windows;
using System.Windows.Media;

namespace PathSnip.Tools
{
    /// <summary>
    /// 标注工具类型枚举
    /// </summary>
    public enum AnnotationType
    {
        None,
        Rectangle,
        Arrow,
        Text,
        Mosaic,
        StepMarker
    }

    /// <summary>
    /// 标注工具接口（契约）
    /// 定义所有标注工具必须实现的行为
    /// </summary>
    public interface IAnnotationTool
    {
        /// <summary>
        /// 工具类型
        /// </summary>
        AnnotationType Type { get; }

        /// <summary>
        /// 是否为轨迹型工具（如马赛克），轨迹型工具使用 Polyline 记录绘制轨迹
        /// </summary>
        bool IsStrokeBased { get; }

        /// <summary>
        /// 工具被选中时的初始化
        /// </summary>
        /// <param name="context">绘图上下文环境</param>
        void OnSelected(AnnotationToolContext context);

        /// <summary>
        /// 工具被取消选中时的清理
        /// </summary>
        void OnDeselected();

        /// <summary>
        /// 鼠标按下事件
        /// </summary>
        /// <param name="position">鼠标位置（相对于画布）</param>
        void OnMouseDown(Point position);

        /// <summary>
        /// 鼠标移动事件
        /// </summary>
        /// <param name="position">鼠标位置（相对于画布）</param>
        void OnMouseMove(Point position);

        /// <summary>
        /// 鼠标抬起事件
        /// </summary>
        /// <param name="position">鼠标位置（相对于画布）</param>
        void OnMouseUp(Point position);

        /// <summary>
        /// 取消当前绘制（如右键取消或 ESC）
        /// </summary>
        void Cancel();

        /// <summary>
        /// 绘制完成时的回调
        /// </summary>
        /// <param name="pushToUndo">用于将撤销操作推入栈的回调</param>
        void OnComplete(Action<UndoAction> pushToUndo);
    }
}
