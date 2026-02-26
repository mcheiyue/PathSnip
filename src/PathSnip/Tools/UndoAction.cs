using System;

namespace PathSnip.Tools
{
    /// <summary>
    /// 撤销/重做操作记录
    /// </summary>
    public record UndoAction(Action Undo, Action Redo);
}
