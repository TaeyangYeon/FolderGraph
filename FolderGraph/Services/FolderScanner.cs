using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FolderGraph.Helpers;
using FolderGraph.Models;
using FolderGraph.Services.Abstractions;

namespace FolderGraph.Services
{
    /// <summary>
    /// System.IO 기반 폴더 스캐너. BFS(너비 우선)로 순회하며,
    /// MaxNodes 한계에 도달하면 스캔을 중단한다.
    /// 접근 권한 오류 등은 해당 폴더만 건너뛰고 계속 진행한다.
    /// </summary>
    public class FolderScanner : IFolderScanner
    {
        public Task<GraphData> ScanAsync(
            string rootPath,
            int maxDepth,
            bool includeHidden,
            CancellationToken cancellationToken)
        {
            // 동기 IO 작업을 백그라운드 스레드로 넘겨 UI 블로킹 방지.
            return Task.Run(() => Scan(rootPath, maxDepth, includeHidden, cancellationToken),
                            cancellationToken);
        }

        private GraphData Scan(
            string rootPath,
            int maxDepth,
            bool includeHidden,
            CancellationToken cancellationToken)
        {
            var data = new GraphData { RootPath = rootPath };

            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            {
                throw new DirectoryNotFoundException("경로를 찾을 수 없습니다: " + rootPath);
            }

            // BFS 큐: (디렉터리 경로, 부모 모델, 깊이)
            var queue = new Queue<ScanItem>();

            // 루트 바로 아래 항목(Depth 0)부터 큐에 적재
            EnqueueChildren(queue, rootPath, null, 0, includeHidden, maxDepth, cancellationToken);

            while (queue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (data.AllNodes.Count >= AppConstants.MaxNodes)
                {
                    data.TruncatedByLimit = true;
                    break;
                }

                ScanItem item = queue.Dequeue();
                FileNodeModel node = CreateNode(item);

                data.AllNodes.Add(node);

                if (item.Parent == null)
                {
                    data.RootNodes.Add(node);
                }
                else
                {
                    item.Parent.Children.Add(node);
                }

                // 폴더이고 아직 최대 깊이 이전이면 그 자식들을 큐에 추가
                if (node.Type == NodeType.Folder && item.Depth < maxDepth - 1)
                {
                    EnqueueChildren(queue, node.FullPath, node, item.Depth + 1,
                                    includeHidden, maxDepth, cancellationToken);
                }
            }

            return data;
        }

        /// <summary>
        /// 지정한 디렉터리의 직속 자식(폴더+파일)을 큐에 적재한다.
        /// 접근 불가 폴더는 조용히 건너뛴다.
        /// </summary>
        private void EnqueueChildren(
            Queue<ScanItem> queue,
            string directoryPath,
            FileNodeModel parent,
            int depth,
            bool includeHidden,
            int maxDepth,
            CancellationToken cancellationToken)
        {
            if (depth >= maxDepth)
            {
                return;
            }

            string[] directories;
            string[] files;

            try
            {
                directories = Directory.GetDirectories(directoryPath);
                files = Directory.GetFiles(directoryPath);
            }
            catch (UnauthorizedAccessException)
            {
                return; // 접근 권한 없음 → 건너뜀
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (IOException)
            {
                return;
            }

            foreach (string dir in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!includeHidden && IsHidden(dir))
                {
                    continue;
                }
                queue.Enqueue(new ScanItem
                {
                    Path = dir,
                    Parent = parent,
                    Depth = depth,
                    Type = NodeType.Folder
                });
            }

            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!includeHidden && IsHidden(file))
                {
                    continue;
                }
                queue.Enqueue(new ScanItem
                {
                    Path = file,
                    Parent = parent,
                    Depth = depth,
                    Type = NodeType.File
                });
            }
        }

        private FileNodeModel CreateNode(ScanItem item)
        {
            long size = 0;
            if (item.Type == NodeType.File)
            {
                try
                {
                    size = new FileInfo(item.Path).Length;
                }
                catch (IOException)
                {
                    size = 0;
                }
                catch (UnauthorizedAccessException)
                {
                    size = 0;
                }
            }

            return new FileNodeModel
            {
                Name = Path.GetFileName(item.Path),
                FullPath = item.Path,
                Type = item.Type,
                Depth = item.Depth,
                SizeBytes = size,
                Parent = item.Parent
            };
        }

        private static bool IsHidden(string path)
        {
            try
            {
                FileAttributes attr = File.GetAttributes(path);
                return (attr & FileAttributes.Hidden) == FileAttributes.Hidden;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>스캔 중 내부적으로 쓰는 작업 항목.</summary>
        private class ScanItem
        {
            public string Path;
            public FileNodeModel Parent;
            public int Depth;
            public NodeType Type;
        }
    }
}
