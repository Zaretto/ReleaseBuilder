using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReleaseBuilder
{
    public class Artefact
    {
        public string Create;
        public string Clean;

        public Artefact(string create, string clean)
        {
            Create = create;
            Clean = clean;
        }
    }
}
