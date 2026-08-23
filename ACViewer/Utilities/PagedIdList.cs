using System;
using System.Collections.Generic;
using System.Linq;

namespace ACViewer.Utilities
{
    public readonly record struct PagedIdResult(
        IReadOnlyList<uint> Items,
        int Page,
        int PageCount,
        int Total,
        int FirstDisplayIndex,
        int LastDisplayIndex)
    {
        public bool HasPrevious => Page > 0;
        public bool HasNext => Page + 1 < PageCount;
        public string StatusText => Total == 0 ? "No matching items" : $"{FirstDisplayIndex:N0}-{LastDisplayIndex:N0} of {Total:N0}";
    }

    public static class PagedIdList
    {
        public static PagedIdResult Build(IEnumerable<uint> ids, string filter, int requestedPage, int pageSize)
        {
            var normalizedFilter = (filter ?? string.Empty).Trim();
            var filtered = string.IsNullOrWhiteSpace(normalizedFilter)
                ? ids.ToList()
                : ids.Where(id => HexId.Format(id).Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            var total = filtered.Count;
            var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)Math.Max(1, pageSize)));
            var page = Math.Clamp(requestedPage, 0, pageCount - 1);
            var items = filtered.Skip(page * pageSize).Take(pageSize).ToList();
            var first = total == 0 ? 0 : page * pageSize + 1;
            var last = Math.Min(total, (page + 1) * pageSize);

            return new PagedIdResult(items, page, pageCount, total, first, last);
        }
    }
}
