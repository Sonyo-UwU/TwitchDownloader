using TwitchDownloaderCore.TwitchObjects;

namespace TwitchDownloaderCore
{
    public sealed class PronounsManager
    {
        private readonly Dictionary<string, string> _cache = [];
        private Dictionary<string, Pronoun> _pronouns = null;

        public string GetPronouns(string login)
        {
            if (_cache.TryGetValue(login, out var pronounsString))
            {
                return pronounsString;
            }

            return "";
        }

        public async Task FetchPronouns(IEnumerable<string> logins)
        {
            foreach (var chunk in logins.Distinct().Where(l => l is not null && !_cache.ContainsKey(l)).Chunk(10))
            {
                foreach (var (login, pronouns) in await Task.WhenAll(chunk.Select(async l => (l, await TwitchHelper.GetUserPronouns(l)))))
                {
                    var value = await formatPronouns(pronouns);
                    _cache[login] = value;
                }
            }
        }

        public async Task<string> GetPronounsAsync(string login)
        {
            if (_cache.TryGetValue(login, out var pronounsString))
            {
                return pronounsString;
            }

            var pronouns = await TwitchHelper.GetUserPronouns(login);
            var value = await formatPronouns(pronouns);
            return _cache[login] = value;
        }

        private async Task<string> formatPronouns(UserPronouns pronouns)
        {
            if (pronouns is null)
                return "";

            _pronouns ??= await TwitchHelper.GetAllPronouns();

            var pronoun = _pronouns[pronouns.pronoun_id];
            var first = pronoun.subject;
            var second = pronouns.alt_pronoun_id is not null ? _pronouns[pronouns.alt_pronoun_id].subject : (pronoun.singular ? null : pronoun.@object);
            return second is not null ? $"{first}/{second}" : first;
        }


        /*

        // Please use event listeners to run functions.
        document.addEventListener('onLoad', async function (obj) {
            window.allPronouns = await fetch('https://api.pronouns.alejo.io/v1/pronouns').then(r => r.json());
            window.pronounsMap = new Map();
        });

        document.addEventListener('onEventReceived', async function (obj) {
            const displayName = obj.detail.tags['display-name'];
            const userPronouns = await getPronouns(displayName.toLowerCase());
            if (!userPronouns)
                return;

            const allMessages = document.getElementById('log').querySelectorAll(`[data-from=${displayName}]`);
            if (allMessages.length <= 0)
                return;

            const span = allMessages.values().toArray().at(-1).getElementsByClassName('pronouns')[0];
            span.textContent = userPronouns;
            span.style.display = 'unset';
        });

        async function getPronouns(login) {
            if (window.pronounsMap.has(login))
                return window.pronounsMap.get(login);

            let result = null;
            const userPronouns = await fetch('https://api.pronouns.alejo.io/v1/users/' + login).then(r => r.json()).catch(() => {});
            if (userPronouns) {
                const pronounsObj = window.allPronouns[userPronouns.pronoun_id];
                const first = pronounsObj.subject;
                const second = userPronouns.alt_pronoun_id ? allPronouns[userPronouns.alt_pronoun_id].subject : (pronounsObj.singular ? null : pronounsObj.object);
                result = second ? `${first}/${second}` : first;
            }
            window.pronounsMap.set(login, result);
            return result;
        }

         */
    }
}
