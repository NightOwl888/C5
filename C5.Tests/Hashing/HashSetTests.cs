// This file is part of the C5 Generic Collection Library for C# and CLI
// See https://github.com/sestoft/C5/blob/master/LICENSE.txt for licensing details.

using C5.Tests.Templates.Set;
using System.Collections.Generic;

namespace C5.Tests.Hashing
{
    public class SCG_ISet : SCG_ISetBase
    {
        public override ISet<string> CreateSet(IEqualityComparer<string> equalityComparer, params string[] values)
        {
            var set = new HashSet<string>(equalityComparer);
            set.UnionWith(values);
            return set;
        }
    }
}
