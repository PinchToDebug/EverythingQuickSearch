using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace EverythingQuickSearch
{
    public class EverythingService : IDisposable
    {
        private readonly Dictionary<int, TaskCompletionSource<List<FileItem>>> _pendingQueries = new();
        private int _nextReplyId = 1000;
        private readonly HwndSource _source;
        private readonly IntPtr _hwnd;
        private const int REPLY_ID = 999;

        public EverythingService(Window window)
        {
            _hwnd = new WindowInteropHelper(window).Handle;
            _source = HwndSource.FromHwnd(_hwnd);
            _source.AddHook(WndProc);

            Everything_SetReplyWindow(_hwnd);
            Everything_SetReplyID(REPLY_ID);
        }

        public Task<List<FileItem>> SearchAsync(string searchText, int setSort, int offset, int maxResults, bool enableRegex)
        {
            int replyId = Interlocked.Increment(ref _nextReplyId);

            var tcs = new TaskCompletionSource<List<FileItem>>();
            _pendingQueries[replyId] = tcs;

            Everything_SetSearchW(searchText);
            Everything_SetOffset((uint)offset);
            Everything_SetMax((uint)maxResults);

            Everything_SetRequestFlags(
                EVERYTHING_REQUEST_FILE_NAME |
                EVERYTHING_REQUEST_PATH |
                EVERYTHING_REQUEST_DATE_MODIFIED |
                EVERYTHING_REQUEST_SIZE);
            Everything_SetRegex(enableRegex);
            Everything_SetReplyID(replyId);
            Everything_SetSort(setSort);
            Everything_QueryW(false);

            return tcs.Task;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            foreach (var kvp in _pendingQueries.ToList())
            {
                if (Everything_IsQueryReply((uint)msg, wParam, lParam, kvp.Key))
                {
                    handled = true;
                    ProcessResults(kvp.Key);
                    break;
                }
            }
            return IntPtr.Zero;
        }

        private void ProcessResults(int replyId)
        {
            if (!_pendingQueries.TryGetValue(replyId, out var tcs))
                return;

            try
            {
                uint count = Everything_GetNumResults();
                var list = new List<FileItem>();
                var sb = new StringBuilder(400);

                for (uint i = 0; i < count; i++)
                {
                    sb.Clear();
                    Everything_GetResultFullPathName(i, sb, 400);
                    string fullPath = sb.ToString();

                    string fileName = System.IO.Path.GetFileName(fullPath);

                    if (string.IsNullOrEmpty(fileName))
                    {
                        continue;
                    }

                    Everything_GetResultDateModified(i, out long modDate);
                    Everything_GetResultSize(i, out long size);

                    list.Add(new FileItem
                    {
                        FullPath = fullPath,
                        Name = fileName,
                        ModificationDate = modDate > 0
                            ? DateTime.FromFileTime(modDate).ToString()
                            : "N/A",
                        Size = size
                    });
                }

                tcs.TrySetResult(list);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
            finally
            {
                _pendingQueries.Remove(replyId);
            }
        }
        public void Dispose()
        {
            _source.RemoveHook(WndProc);
        }

        #region Everything SDK Imports

        [DllImport("Everything64.dll")]
        public static extern void Everything_SetSort(int dwSortType);

        [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
        private static extern void Everything_SetSearchW(string lpSearchString);

        [DllImport("Everything64.dll")]
        private static extern void Everything_SetOffset(uint dwOffset);

        [DllImport("Everything64.dll")]
        private static extern void Everything_SetMax(uint dwMax);
        [DllImport("Everything64.dll")]
        public static extern void Everything_SetRegex(bool bEnable);

        [DllImport("Everything64.dll")]
        private static extern void Everything_SetRequestFlags(uint dwFlags);

        [DllImport("Everything64.dll")]
        private static extern void Everything_SetReplyWindow(IntPtr hwnd);

        [DllImport("Everything64.dll")]
        private static extern void Everything_SetReplyID(int nId);

        [DllImport("Everything64.dll")]
        private static extern bool Everything_QueryW(bool bWait);

        [DllImport("Everything64.dll")]
        private static extern bool Everything_IsQueryReply(
            uint message,
            IntPtr wParam,
            IntPtr lParam,
            int nId);

        [DllImport("Everything64.dll")]
        private static extern uint Everything_GetNumResults();

        [DllImport("Everything64.dll")]
        private static extern void Everything_GetResultFullPathName(
            uint index,
            StringBuilder sb,
            int max);

        [DllImport("Everything64.dll")]
        private static extern IntPtr Everything_GetResultFileName(uint index);

        [DllImport("Everything64.dll")]
        private static extern void Everything_GetResultDateModified(
            uint index,
            out long dateModified);

        [DllImport("Everything64.dll")]
        private static extern void Everything_GetResultSize(
            uint index,
            out long size);


        private const uint EVERYTHING_REQUEST_FILE_NAME = 0x00000001;
        private const uint EVERYTHING_REQUEST_PATH = 0x00000002;
        private const uint EVERYTHING_REQUEST_SIZE = 0x00000010;
        private const uint EVERYTHING_REQUEST_DATE_MODIFIED = 0x00000040;

        #endregion
    }

}