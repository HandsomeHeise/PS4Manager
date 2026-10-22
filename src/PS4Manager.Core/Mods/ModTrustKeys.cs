using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PS4Manager.Core.Mods
{
    public static class ModTrustKeys
    {
        // Public RSA key obtained via the authenticated deployment operator channel.
        // Independent from application-update keys. Catalog responses cannot add trust.
        public static readonly IReadOnlyDictionary<string,string> Production =
            new ReadOnlyDictionary<string,string>(new Dictionary<string,string> {
                { "503d131c6d8d979e", "<RSAKeyValue><Modulus>" +
                  "qQ8THb+vM8QQkP3Wy5AmN12h0fDVtyv0b83+64PRu8YsTG+ZOUkrIgnqBJPTaHt09bTWa65dxdgT8yEmG4DvPU+Ie/z65CE3CGeAaD/I7f+2IY6gvEQuDIHAt+3Ty3sKv3asr1/1AmtUeFHTzJe8KndailTEpWyR1zEj/H31ElDxgwcDjqq+vOOlyz5YCYLAlsa27qO8YszXm1j7N7PIaB55Dhgcx/yizG9tzqFA+DvSfH/ybkJjbqM6Ngg5xvQET0DMTBPX6IbEVG5vXqvw8ZzMbgZe2jDvd+pjccbkalSZCxMKWzBm9Kzgi0As9CbUJZkoA4M/OJTpTDlG4BOTs06fFPYYhIz1wj8ZrUN0hp/vEI0m2bA00vS2PzI8XVHgxaz3mEg22ox6FimrkMDkBG0zJrZImcWXLZmwFvuLtQLzIKs49c8Rg5O1rZm3smri2SQCv05U1wRTWxyAP9m6zCbxGrzcNRzvyVCzZMNsAf9aJMfPjZAwmKdFDLxNGql3" +
                  "</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>" }
            });
    }
}
