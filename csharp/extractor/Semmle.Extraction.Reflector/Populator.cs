using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Semmle.Extraction.Reflector
{
    public delegate void KeyWriter(object obj, TextWriter writer);

    public class Populator
    {
        public TextWriter Trap { get; }
        readonly Model model;
        readonly IConfiguration configuration;
        readonly Dictionary<object, int> identityMapper;
        int nextId;

        public Populator(Model m, TextWriter tw)
        {
            model = m;
            configuration = model.Configuration;
            Trap = tw;
            // Trap = Console.Out;
            identityMapper = new Dictionary<object, int>(configuration);
            nextId = 1;
        }
    }
}
