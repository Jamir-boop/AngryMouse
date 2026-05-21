namespace AngryMouse.Cursors
{
    internal sealed class CursorPrewarmProgress
    {
        public CursorPrewarmProgress(int completed, int total, string currentItem, bool isComplete)
        {
            Completed = completed;
            Total = total;
            CurrentItem = currentItem;
            IsComplete = isComplete;
        }

        public int Completed { get; }

        public int Total { get; }

        public string CurrentItem { get; }

        public bool IsComplete { get; }
    }
}
