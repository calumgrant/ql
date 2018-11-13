using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

partial class Populator
{
    TextWriter writer;

    Dictionary<object, int> labels;
    int nextLabel;

    bool GetOrCreateLabel(object obj, out int label)
    {
        if (labels.TryGetValue(obj, out label))
            return true;
        else
        {
            labels.Add(obj, label = nextLabel++);
            return false;
        }
    }
}
