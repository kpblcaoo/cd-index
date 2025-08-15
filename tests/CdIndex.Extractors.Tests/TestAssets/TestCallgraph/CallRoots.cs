namespace TestCallgraph;

public class RootClass
{
    public void A() { B(); C(); }
    public void B() { D(); }
    public void C() { }
    public void D() { }
    public void Over(int x) { }
    public void Over(string s) { }
}

public class ExternalCalls
{
    public void UseLinq()
    {
        var list = new[]{1,2,3};
        list.Select(x=>x+1).ToArray(); // external
    }
}
