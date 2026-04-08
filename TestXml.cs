using System;
using System.Xml;
class Test {
    static void Main() {
        var doc1 = new XmlDocument();
        doc1.LoadXml("<root>value1</root>");
        var doc2 = new XmlDocument();
        doc2.LoadXml("<root>value2</root>");
        Console.WriteLine($"doc1 OuterXml: {doc1.DocumentElement.OuterXml}");
        Console.WriteLine($"doc2 OuterXml: {doc2.DocumentElement.OuterXml}");
        Console.WriteLine($"Is IComparable: {doc1.DocumentElement is IComparable}");
        Console.WriteLine($"Equal OuterXml: {doc1.DocumentElement.OuterXml == doc2.DocumentElement.OuterXml}");
    }
}
