using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Clans {
    // The JSON Clans.API hands out, written by hand: small shapes, numbers in the invariant culture.
    //
    //   new JsonOut().Object().Field("clan", 3).Array("tabs").Object().Field("index", 0).End().End().End()
    //
    // Object() and Array() open a container (with a key when given one, inside an object); End() closes the
    // innermost one.
    internal sealed class JsonOut {
        private readonly StringBuilder text = new StringBuilder();
        private readonly Stack<char> closers = new Stack<char>();
        private bool first = true;

        internal JsonOut Object() { Next(); return Open('{', '}'); }
        internal JsonOut Object(string key) { Name(key); return Open('{', '}'); }
        internal JsonOut Array() { Next(); return Open('[', ']'); }
        internal JsonOut Array(string key) { Name(key); return Open('[', ']'); }

        internal JsonOut End() {
            text.Append(closers.Pop());
            first = false;
            return this;
        }

        internal JsonOut Field(string key, string value) { Name(key); Quote(value); return this; }
        internal JsonOut Field(string key, long value) { Name(key); text.Append(value.ToString(CultureInfo.InvariantCulture)); return this; }
        internal JsonOut Field(string key, bool value) { Name(key); text.Append(value ? "true" : "false"); return this; }

        internal JsonOut Value(string value) { Next(); Quote(value); return this; }
        internal JsonOut Value(long value) { Next(); text.Append(value.ToString(CultureInfo.InvariantCulture)); return this; }

        public override string ToString() => text.ToString();

        private JsonOut Open(char opener, char closer) {
            text.Append(opener);
            closers.Push(closer);
            first = true;
            return this;
        }

        private void Next() {
            if (first == false) { text.Append(','); }
            first = false;
        }

        private void Name(string key) {
            Next();
            Quote(key);
            text.Append(':');
        }

        private void Quote(string value) {
            text.Append('"');
            foreach (char c in value ?? "") {
                switch (c) {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    case '\b': text.Append("\\b"); break;
                    case '\f': text.Append("\\f"); break;
                    default:
                        if (c < ' ') {
                            text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        } else {
                            text.Append(c);
                        }
                        break;
                }
            }
            text.Append('"');
        }

        // {"Wood":10,"Stone":5}: item prefab names to amounts. Null when the text is not that shape, or any amount
        // is not a whole number above 0.
        internal static Dictionary<string, int> ParseItems(string json) {
            if (string.IsNullOrWhiteSpace(json)) { return null; }
            object parsed;
            try {
                parsed = SimpleJson.SimpleJson.DeserializeObject(json);
            } catch (Exception) {
                return null;
            }
            if (!(parsed is IDictionary<string, object> values) || values.Count == 0) { return null; }
            Dictionary<string, int> items = new Dictionary<string, int>();
            foreach (KeyValuePair<string, object> entry in values) {
                if (string.IsNullOrEmpty(entry.Key)) { return null; }
                double amount;
                switch (entry.Value) {
                    case long l: amount = l; break;
                    case int i: amount = i; break;
                    case double d: amount = d; break;
                    case decimal m: amount = (double)m; break;
                    default: return null;
                }
                if (amount < 1 || amount > int.MaxValue || Math.Floor(amount) != amount) { return null; }
                items[entry.Key] = (int)amount;
            }
            return items;
        }
    }
}
