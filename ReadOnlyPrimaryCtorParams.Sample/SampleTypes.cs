using ReadOnlyPrimaryCtorParams;

namespace ReadOnlyPrimaryCtorParams.Sample;

public class Bar
{
    public string Name { get; set; } = nameof(Name);
}

public partial class NestedParent
{
    public partial class NestedChild([ReadOnly] Bar bar)
    {
        public void Get()
        {
            Console.WriteLine(bar.Name);
            bar = null!;
        }
    }
}


public partial class FooClass([ReadOnly] Bar bar)
{
    public void Get()
    {
        Console.WriteLine(bar.Name);
    }
}

public partial struct FooStruct([ReadOnly] Bar bar)
{
    public void Get()
    {
        Console.WriteLine(bar.Name);
    }
}

public partial record FooRecord([ReadOnly] Bar bar)
{
    public void Get()
    {
        Console.WriteLine(bar.Name);
    }
}

public partial record struct FooRecordStruct([ReadOnly] Bar bar)
{
    public void Get()
    {
        Console.WriteLine(bar.Name);
    }
}

public partial class FooClassGeneric<T>([ReadOnly] Bar bar)
{
    public void Get()
    {
        Console.WriteLine(bar.Name);
    }
}

public partial struct FooStructGeneric<T>([ReadOnly] Bar bar)
{
    public void Get()
    {
        Console.WriteLine(bar.Name);
    }
}

public partial record FooRecordGeneric<T>([ReadOnly] Bar bar)
{
    public void Get()
    {
        Console.WriteLine(bar.Name);
    }
}

public partial record struct FooRecordStructGeneric<T1, T2, T3>([ReadOnly] Bar bar)
{
    public void Get()
    {
        Console.WriteLine(bar.Name);
    }
}
