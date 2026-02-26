using System;

namespace PathSnip.Tools
{
    /// <summary>
    /// 撤销/重做操作记录
    /// </summary>
    public class UndoAction
    {
        public Action Undo { get; }
        public Action Redo { get; }

        public UndoAction(Action undo, Action redo)
        {
            Undo = undo;
            Redo = redo;
        }
    }
}
