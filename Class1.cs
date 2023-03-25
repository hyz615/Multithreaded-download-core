using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace MultiThreadedDownloader
{
    public class MultiThreadedDownloader
    {
        private const int DefaultBufferSize = 4096;

        private readonly Uri downloadUrl;
        private readonly string filePath;
        private readonly int threadCount;
        private readonly IWebProxy proxy;
        private readonly WebHeaderCollection headers;
        private readonly CancellationToken cancellationToken;

        public MultiThreadedDownloader(Uri downloadUrl, string filePath, int threadCount = 4, IWebProxy proxy = null, WebHeaderCollection headers = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            this.downloadUrl = downloadUrl;
            this.filePath = filePath;
            this.threadCount = threadCount;
            this.proxy = proxy;
            this.headers = headers;
            this.cancellationToken = cancellationToken;
        }

        public async Task DownloadAsync(IProgress<long> progress = null)
        {
            long totalFileSize = GetTotalFileSize();
            long[] rangeStartPositions = CalculateRangeStartPositions(totalFileSize);

            Task[] tasks = new Task[threadCount];

            for (int i = 0; i < threadCount; i++)
            {
                int threadIndex = i;
                tasks[i] = Task.Run(() => DownloadFilePart(rangeStartPositions[threadIndex], totalFileSize, progress), cancellationToken);
            }

            await Task.WhenAll(tasks);

            MergeFileParts();
        }

        private long GetTotalFileSize()
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(downloadUrl);
            request.Method = "HEAD";

            if (proxy != null)
            {
                request.Proxy = proxy;
            }

            if (headers != null)
            {
                request.Headers = headers;
            }

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                return response.ContentLength;
            }
        }

        private long[] CalculateRangeStartPositions(long totalFileSize)
        {
            long[] rangeStartPositions = new long[threadCount];
            long chunkSize = totalFileSize / threadCount;

            for (int i = 0; i < threadCount; i++)
            {
                rangeStartPositions[i] = i * chunkSize;
            }

            rangeStartPositions[threadCount - 1] = totalFileSize - chunkSize;

            return rangeStartPositions;
        }

        private void DownloadFilePart(long startPos, long endPos, IProgress<long> progress)
        {
            byte[] buffer = new byte[DefaultBufferSize];
            int bytesRead;

            string tempFilePath = filePath + ".part" + startPos.ToString();
            bool resume = File.Exists(tempFilePath);

            HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(downloadUrl);
            webRequest.Method = "GET";

            if (resume)
            {
                webRequest.AddRange(startPos + new FileInfo(tempFilePath).Length, endPos);
            }
            else
            {
                webRequest.AddRange(startPos, endPos);
            }

            if (proxy != null)
            {
                webRequest.Proxy = proxy;
            }

            if (headers != null)
            {
                webRequest.Headers = headers;
            }

            using (HttpWebResponse response = (HttpWebResponse)webRequest.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (FileStream fileStream = new FileStream(tempFilePath, resume ? FileMode.Append : FileMode.Create, FileAccess.Write))
            {
                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    fileStream.Write(buffer, 0, bytesRead);
                    progress?.Report(bytesRead);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
        }

        private void MergeFileParts()
        {
            using (FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                for (int i = 0; i < threadCount; i++)
                {
                    string tempFilePath = filePath + ".part" + i.ToString();

                    using (FileStream tempFileStream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read))
                    {
                        byte[] buffer = new byte[DefaultBufferSize];
                        int bytesRead;

                        while ((bytesRead = tempFileStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            fileStream.Write(buffer, 0, bytesRead);
                        }
                    }

                    File.Delete(tempFilePath);
                }
            }
        }
    }
}

