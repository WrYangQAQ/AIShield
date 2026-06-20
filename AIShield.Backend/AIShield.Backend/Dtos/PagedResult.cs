namespace AIShield.Backend.Dtos
{
    public class PagedResult<T>
    {
        // 当前页数据
        public List<T> Items { get; set; } = new();

        // 总条数
        public int Total { get; set; }

        // 当前页码
        public int PageIndex { get; set; }

        // 每页条数
        public int PageSize { get; set; }
    }
}
