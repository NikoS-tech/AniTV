namespace AniTV;

public sealed record GenreDefinition(string Name, string VostSlug, string BestPath);

public static class CatalogGenres
{
    // Only direct genre equivalents which have working catalog pages on both sources.
    public static IReadOnlyList<GenreDefinition> All { get; } =
    [
        new("Боевые искусства", "boyevyye-iskusstva", "anime1-online/boevye-iskusstva"),
        new("Драма", "drama", "anime1-online/drama-ab"),
        new("Детектив", "detektiv", "anime1-online/detektiv"),
        new("История", "istoriya", "anime1-online/istoriya"),
        new("Комедия", "komediya", "anime1-online/komediya"),
        new("Меха", "mekha", "anime1-online/meha"),
        new("Мистика", "mistika", "anime1-online/mistika"),
        new("Махо-сёдзё", "makho-sedze", "anime1-online/maho-sedze"),
        new("Музыкальный", "muzykalnyy", "anime1-online/muzykalnyy"),
        new("Повседневность", "povsednevnost", "anime1-online/povsednevnost"),
        new("Приключения", "priklyucheniya", "anime1-online/priklyucheniya"),
        new("Пародия", "parodiya", "anime1-online/parodiya-ab"),
        new("Романтика", "romantika", "anime1-online/romantika-ab"),
        new("Сёнэн", "senen", "anime1-online/senen-ab"),
        new("Сёдзё", "sedze", "anime1-online/sedze"),
        new("Спорт", "sport", "anime1-online/sport"),
        new("Сказка", "skazka", "anime1-online/skazka"),
        new("Триллер", "triller", "anime1-online/triller"),
        new("Ужасы", "uzhasy", "anime1-online/uzhasy"),
        new("Фантастика", "fantastika", "anime1-online/fantastika"),
        new("Фэнтези", "fentezi", "anime1-online/fentezi"),
        new("Школа", "shkola", "anime1-online/shkola"),
        new("Этти", "etti", "anime1-online/etti")
    ];
}

public sealed class GenreFilterItem : System.ComponentModel.INotifyPropertyChanged
{
    public GenreDefinition? Genre { get; init; }
    public int ColorIndex { get; init; }
    public string Name => Genre?.Name ?? "Все жанры";
    public GenreChip Colors => Genre is null ? new(Name,"#252938","#565D75","#E5E7EE") : GenreChip.CreateFilter(Name,ColorIndex);
    bool selected;
    public bool IsSelected { get => selected; set { selected=value; PropertyChanged?.Invoke(this,new(nameof(IsSelected))); } }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
