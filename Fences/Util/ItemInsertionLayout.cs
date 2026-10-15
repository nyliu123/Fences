using System;
using System.Collections.Generic;
using System.Drawing;

namespace NoFences.Util
{
    internal struct ItemInsertionTarget
    {
        public int Index;
        public Rectangle Marker;
    }

    internal static class ItemInsertionLayout
    {
        public static ItemInsertionTarget Locate(Point cursor, int count, int width,
            int titleHeight, int scrollOffset, int padding, int itemWidth, int itemHeight, int spacing)
        {
            var columns = Math.Max(1, (width - padding - itemWidth) / (itemWidth + spacing) + 1);
            var row = Math.Max(0, (cursor.Y - titleHeight + scrollOffset - padding + spacing / 2) /
                (itemHeight + spacing));
            row = Math.Min(row, Math.Max(0, (count - 1) / columns));
            var rowStart = row * columns;
            var rowCount = Math.Min(columns, count - rowStart);
            var firstBoundary = padding - spacing / 2;
            var column = (int)Math.Round((cursor.X - firstBoundary) / (double)(itemWidth + spacing),
                MidpointRounding.AwayFromZero);
            column = Math.Max(0, Math.Min(rowCount, column));
            var markerX = Math.Max(2, Math.Min(width - 4, firstBoundary + column * (itemWidth + spacing)));
            return new ItemInsertionTarget
            {
                Index = rowStart + column,
                Marker = new Rectangle(markerX, titleHeight + padding - scrollOffset +
                    row * (itemHeight + spacing), 2, itemHeight)
            };
        }

        public static bool Move(IList<string> paths, int sourceIndex, int insertionIndex)
        {
            if (sourceIndex < 0 || sourceIndex >= paths.Count || insertionIndex < 0 || insertionIndex > paths.Count)
                return false;
            var destinationIndex = insertionIndex > sourceIndex ? insertionIndex - 1 : insertionIndex;
            if (destinationIndex == sourceIndex)
                return false;
            var path = paths[sourceIndex];
            paths.RemoveAt(sourceIndex);
            paths.Insert(destinationIndex, path);
            return true;
        }
    }
}
