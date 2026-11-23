// Polish texts of the mod's page. Another language is another file like this one, loaded by index.html
// (see docs/site/i18n.js). Strings may hold simple HTML (<code>, <b>, <a>); functions build sentences from settings.
(function () {
  const W = "https://github.com/Mrucznik/vs-temporal-rebirth/wiki/";
  const wiki = (page, label) => `<a href="${W}${page}" target="_blank" rel="noopener">${label}</a>`;
  const few = (n, a, b) => {
    const t = n % 10, h = n % 100;
    return Number.isInteger(n) && t >= 2 && t <= 4 && (h < 12 || h > 14) ? a : b;
  };
  // "1 godzina", "2 godziny", "5 godzin", "1,5 godziny": a fraction takes its own form.
  const count = (n, one, two, five, part) =>
    String(n).replace(".", ",") + " " + (!Number.isInteger(n) ? part : n === 1 ? one : few(n, two, five));
  // After "w promieniu": "1 bloku", "24 bloków".
  const blocks = (n) => n + (n === 1 ? " bloku" : " bloków");
  // The place of an afterlife or a return, in the forms the sentences below need.
  const places = {
    Body: { from: "miejsca śmierci", at: "w miejscu śmierci" },
    Spawn: { from: "punktu odrodzenia", at: "w punkcie odrodzenia" },
    Fixed: { from: "miejsca wyznaczonego przez admina", at: "w miejscu wyznaczonym przez admina" },
  };

  (window.TR_SITE ??= {}).pl = {
    name: "Polski",
    meta: {
      title: "Temporal Rebirth – mod do Vintage Story",
      description: "Mod do Vintage Story. Zębatki temporalne dają dodatkowe życia, a po stracie ostatniego możesz wrócić do gry.",
    },
    nav: {
      skip: "Przejdź do treści", home: "Strona główna", config: "Konfigurator", wiki: "Wiki", menu: "Spis treści", language: "Język",
      sections: { how: "Jak to działa", presets: "Presety", forms: "Formy", rituals: "Rytuały", faq: "Pytania" },
    },
    hero: {
      kicker: "Mod do Vintage Story 1.22.7",
      pitch: "Zębatka temporalna to dodatkowe życie.<br>Po ostatniej śmierci możesz <em>wrócić</em>.",
      lead: "W Vintage Story można ustawić limit żyć. Kto straci ostatnie życie, zwykle nie gra już w tym świecie. Z tym modem zębatki temporalne dają dodatkowe życia. Po ostatniej śmierci przez jakiś czas jesteś duchem, zombie albo wygnańcem, zależnie od serwera. Wracasz, gdy minie czas albo gdy przyjaciel odprawi rytuał.",
      download: "Pobierz z Mod DB", github: "Wydania na GitHubie", config: "Konfigurator", how: "Jak to działa",
      video: "Przyjaciel widzi ognik, bierze zębatkę do ręki i widzi echo. Odprawia rytuał, a gracz wraca do gry.",
      facts: ["6 gotowych presetów", "Obsługuje mod Downed"],
    },
    video: { enlarge: "Powiększ film", close: "Zamknij", play: "Odtwórz", unsupported: "Twoja przeglądarka nie odtworzy tego filmu." },

    how: {
      title: "Jak to działa",
      lead: "Kliknij krok, żeby zobaczyć film. Opis dotyczy domyślnych ustawień. Na twoim serwerze szczegóły mogą być inne.",
      or: "albo",
      steps: [
        { short: "Życia z zębatek", title: "Zębatki dają życia", video: "M3",
          caption: "Gracz używa zębatki i dostaje życie. Ekran śmierci pokazuje, ile mu zostało.",
          text: "Na start masz tyle żyć, ile ustawił serwer. Każda <b>zębatka temporalna</b> daje kolejne. Weź ją do ręki i przytrzymaj prawy przycisk myszy na bloku przez 3,5 sekundy. Zębatka znika, twój punkt odrodzenia przenosi się w to miejsce, a ty dostajesz życie.",
          more: "Liczbę żyć pokazuje ekran śmierci i komenda <code>/rebirth lives</code>. Serwer ustala, ile żyć daje jedna zębatka. Może też ustawić limit żyć z zębatek." },
        { short: "Ostatnia śmierć", title: "Tracisz ostatnie życie", video: "W4",
          caption: "Ognik ducha oczami innego gracza.",
          text: "Dopóki masz życia, odradzasz się jak zwykle. Po stracie ostatniego już się nie odradzasz, tylko domyślnie zostajesz <b>duchem</b>. Inni widzą w miejscu twojej śmierci unoszący się ognik.",
          more: "Wszyscy na serwerze dostają wiadomość, że straciłeś ostatnie życie. Co się dzieje z ekwipunkiem, zależy od zwykłych zasad świata." },
        { short: "Duch, zombie…", title: "Zostajesz duchem", video: "W6",
          caption: "Duch lata, nie przejdzie przez ścianę i zatrzymuje się na barierze.",
          text: "Jako duch latasz i patrzysz, ale niczego nie dotkniesz i nie przejdziesz przez ścianę. Domyślnie możesz odejść najwyżej 24 bloki od miejsca śmierci. Dalej nie puści cię <b>bariera</b>. Z daleka prawie jej nie widać, a z bliska wygląda jak rdzawa ściana z obracającymi się zębatkami.",
          more: "Serwer może zamiast ducha ustawić obserwatora, zombie, wygnanie albo bana na jakiś czas. Opis wszystkich form jest niżej, w części <a href=\"#formy\">Formy</a>." },
        { short: "Czekasz", title: "Czekasz, aż minie czas", video: "W14",
          caption: "Ostatnie sekundy licznika.",
          text: "Licznik u góry ekranu pokazuje, ile zostało. Domyślnie wracasz następnego dnia o 06:00 czasu serwera. Liczy się czas rzeczywisty, więc płynie też wtedy, gdy nie grasz.",
          more: "Jeśli w tym czasie znowu zginiesz, nic się nie zmienia. Nie tracisz życia, a czekanie nie skraca się ani nie wydłuża." },
        { short: "Rytuał", title: "Przyjaciel odprawia rytuał", video: "W15", alt: true,
          caption: "Rytuał przy wyrwie temporalnej.",
          text: "Żywy przyjaciel może sprowadzić cię wcześniej. Staje przy aktywnej <b>wyrwie temporalnej</b>, bierze zębatkę temporalną do ręki i przytrzymuje prawy przycisk myszy na bloku przez 3,5 sekundy. Zębatka znika, a ty wracasz.",
          more: "Gdy przyjaciel trzyma zębatkę, zamiast ognika widzi twoje <b>echo</b>, czyli falującą sylwetkę z twoim nickiem. Dzięki temu wie, kogo wskrzesza. Więcej w części <a href=\"#rytualy\">Rytuały</a>." },
        { short: "Powrót", title: "Wracasz do gry", video: "W10",
          caption: "Licznik dochodzi do zera i gracz wraca.",
          text: "Wracasz z jednym życiem, domyślnie w swoim punkcie odrodzenia. Wszyscy na serwerze dostają wiadomość, że wróciłeś. Znowu możesz zbierać życia z zębatek.",
          more: "Jeśli czas minął, gdy byłeś offline, wracasz zaraz po wejściu na serwer. Admin może przywrócić cię wcześniej komendą <code>/rebirth revive</code>." },
      ],
    },

    presets: {
      title: "Presety",
      lead: "Właściciel serwera wybiera jeden z gotowych presetów albo ustawia wszystko sam w konfiguratorze. Kliknij preset, żeby zobaczyć, co czeka gracza po ostatniej śmierci.",
      rows: { form: "Forma", where: "Miejsce", time: "Czas", back: "Powrót", ritual: "Rytuał" },
      configure: "Otwórz ten preset w konfiguratorze",
      videos: { Classic: "W4", Rescue: "W17", Undead: "M2-zombie", Exile: "M2-exile", Timeout: "W11", Hardcore: "M2-ban" },
      captions: {
        Classic: "Gracz ginie, a w miejscu śmierci zostaje ognik.", Rescue: "Przyjaciel wybiera gracza komendą i odprawia rytuał tam, gdzie stoi.",
        Undead: "Zombie ma zabarwione ciało i poświatę.", Exile: "Na wygnaniu grasz normalnie, ale daleko od bazy i przez określony czas.",
        Timeout: "Rytuał przy wyrwie zdejmuje bana.", Hardcore: "Po ostatniej śmierci serwer rozłącza gracza.",
      },
      // Sentences from the preset's values (schema.js). s = all settings after the preset.
      form: (s) => ({
        Ghost: {
          Nobody: "Duch. Nikt cię nie widzi.",
          Wisp: "Duch. Inni widzą ognik.",
          WispAndEcho: "Duch. Inni widzą ognik, a gracz z zębatką w ręce widzi twoje echo.",
        }[s["Ghost.Visible"]],
        Spectator: "Obserwator. Przelatujesz przez ściany i nikt cię nie widzi.",
        Zombie: s["Zombie.CanUseItems"] ? "Zombie. Możesz używać przedmiotów." : "Zombie. Walczysz gołymi rękami i nie otworzysz skrzyni.",
        Player: "Wygnaniec. Grasz normalnie.",
        Ban: "Ban. Nie wejdziesz na serwer.",
      })[s.OutOfLives],
      where: (s) => {
        if (s.OutOfLives === "Ban") return "Poza serwerem.";
        if (s["Place.At"] === "Zone") return "W strefie z zajętych terenów.";
        const p = places[s["Place.At"]];
        return s["Place.Radius"] === 0
          ? "Zaczynasz " + p.at + ". Możesz odejść dowolnie daleko."
          : "W promieniu " + blocks(s["Place.Radius"]) + " od " + p.from + ".";
      },
      time: (d) => d.kind === "forever" ? "Bez limitu. Czekasz, aż ktoś cię wskrzesi."
        : d.kind === "next" ? "Do " + d.time + " następnego dnia, według czasu serwera."
        : (d.kind === "h" ? count(d.n, "godzina", "godziny", "godzin", "godziny") : count(d.n, "dzień", "dni", "dni", "dnia")) + " czasu rzeczywistego.",
      back: (s, forever) => {
        const lives = s["Return.Lives"] === -1 ? "z pełną pulą żyć" : s["Return.Lives"] === 1 ? "z jednym życiem" : "z " + s["Return.Lives"] + " życiami";
        if (forever) return "Tylko przez rytuał albo admina. Wracasz " + lives + ".";
        const at = places[s["Return.At"]].at;
        return s["Return.DieAtEnd"]
          ? "Gdy minie czas, giniesz, ale ta śmierć nie zabiera życia. Potem pojawiasz się " + at + " " + lives + "."
          : "Gdy minie czas, pojawiasz się " + at + " " + lives + ".";
      },
      ritual: (s) => {
        const w = s["Ritual.Where"];
        if (w === "Disabled") return "Wyłączony. Nikt cię nie wskrzesi.";
        const ban = s.OutOfLives === "Ban";
        if (ban && (w === "NearPlayer" || !s["Ritual.CanLiftBan"])) return "Nie zdejmuje bana.";
        const where = {
          Rift: "Działa przy aktywnej wyrwie temporalnej.",
          NearPlayer: "Przyjaciel musi stanąć obok ciebie, a ty musisz być online.",
          FixedPlace: "Działa w miejscu wyznaczonym przez admina.",
          Anywhere: "Działa wszędzie, gdy przyjaciel wybierze cię komendą <code>/rebirth target</code>.",
        }[w];
        const at = ban ? "Ban znika, a ty wracasz przy następnym wejściu na serwer." : {
          Caster: "Pojawiasz się obok przyjaciela, który odprawił rytuał.",
          InPlace: "Wstajesz tam, gdzie jesteś.",
          Body: "Pojawiasz się w miejscu śmierci.",
          Spawn: "Pojawiasz się w punkcie odrodzenia.",
        }[s["Ritual.ReviveAt"]];
        return where + " " + at;
      },
    },

    forms: {
      title: "Formy",
      lead: "Po ostatniej śmierci zmieniasz się w jedną z form. Wybiera ją serwer. W każdej formie oprócz bana nie wyjdziesz poza wyznaczony obszar, a kolejna śmierć nie zabiera życia i nie skraca kary.",
      more: "Więcej w wiki",
      list: [
        { id: "Ghost", name: "Duch", tag: "domyślna", wiki: "Ghost",
          text: ["Latasz i patrzysz, ale niczego nie dotkniesz i nie przejdziesz przez ścianę. Bariera nie pozwala ci odejść za daleko.",
            "Inni widzą w tym miejscu <b>ognik</b>, małe ciemne światełko z żarem w środku. Nie widać, czyj to duch. Gracz z zębatką temporalną w ręce widzi zamiast ognika <b>echo</b>, czyli twoją sylwetkę z nickiem. Dzięki temu wie, kogo wskrzesić."],
          media: [{ v: "W5", label: "Oczami innych", caption: "Gracz bierze zębatkę do ręki i ognik zmienia się w echo." },
            { v: "W6", label: "Oczami ducha", caption: "Duch lata, nie przejdzie przez ścianę i zatrzymuje się na barierze." }] },
        { id: "Spectator", name: "Obserwator", wiki: "Spectator",
          text: ["Jak duch, tylko przelatujesz przez ściany. Niczego nie dotkniesz i nie odejdziesz za daleko.",
            "Nikt cię nie widzi. Listę graczy, którzy czekają na powrót, pokazuje komenda <code>/rebirth ghosts</code>."],
          media: [{ v: "W7", caption: "Obserwator przelatuje przez ściany." }] },
        { id: "Zombie", name: "Zombie", wiki: "Zombie",
          text: ["Chodzisz po świecie i możesz walczyć, ale domyślnie tylko gołymi rękami. Nie użyjesz przedmiotów, nic nie podniesiesz z ziemi i nie otworzysz skrzyni ani drzwi.",
            "Wszyscy widzą zabarwione ciało i poświatę. Gdy zginiesz, znowu wstajesz jako zombie i przez 5 sekund nic cię nie zrani."],
          media: [{ v: "W8", caption: "Zombie próbuje otworzyć skrzynię i nie może." }] },
        { id: "Exile", name: "Wygnaniec", wiki: "Exile",
          text: ["Grasz normalnie, ale w miejscu wyznaczonym przez admina, zwykle daleko od bazy. Bariera nie wypuści cię stamtąd, dopóki nie minie czas.",
            "W presecie Exile wygnanie trwa 24 godziny. Na koniec giniesz, ale ta śmierć nie zabiera życia. Potem pojawiasz się w punkcie odrodzenia z pełną pulą żyć."],
          media: [{ v: "M4", caption: "Bariera na granicy wygnania i licznik u góry ekranu." },
            { v: "W9", label: "Śmierć na wygnaniu", caption: "Po śmierci na wygnaniu wstajesz na miejscu, a licznik odlicza dalej." }] },
        { id: "Ban", name: "Ban", wiki: "Ban",
          text: ["Chwilę po śmierci serwer cię rozłącza i podaje, kiedy możesz wrócić. Do tego czasu nie wejdziesz na serwer.",
            "Na niektórych serwerach przyjaciel może zdjąć bana rytuałem, nawet gdy jesteś offline. Wtedy wracasz przy następnym wejściu."],
          media: [{ v: "M2-ban", caption: "Serwer rozłącza gracza i podaje czas powrotu." },
            { v: "W11", label: "Rytuał zdejmuje bana", caption: "Przyjaciel zdejmuje bana rytuałem przy wyrwie." }] },
      ],
    },

    rituals: {
      title: "Jak sprowadzić przyjaciela",
      lead: "Rytuałem możesz wskrzesić gracza, któremu skończyły się życia, zanim minie jego czas. Potrzebujesz jednej zębatki temporalnej i musisz żyć. Duch, zombie ani wygnaniec rytuału nie odprawi.",
      steps: [
        "Sprawdź, kto czeka na powrót: <code>/rebirth ghosts</code>. Jeśli czeka kilka osób, wybierz jedną komendą <code>/rebirth target &lt;gracz&gt;</code>.",
        "Idź tam, gdzie na twoim serwerze działa rytuał. Domyślnie to najwyżej 8 bloków od aktywnej wyrwy temporalnej.",
        "Weź zębatkę temporalną do ręki. Zamiast ognika zobaczysz echo z nickiem przyjaciela.",
        "Przytrzymaj prawy przycisk myszy na bloku przez 3,5 sekundy. Zębatka znika, przyjaciel wraca do gry, a wszyscy dostają wiadomość.",
      ],
      fail: "Jeśli zębatka została w ręce, to nikt nie czeka na powrót, trzeba najpierw wybrać gracza albo nie minęła jeszcze przerwa przed kolejnym rytuałem. Jeśli zębatka ustawiła ci punkt odrodzenia, stoisz za daleko od miejsca rytuału.",
      whereTitle: "Gdzie działa rytuał",
      whereLead: "To ustawia serwer. Domyślnie trzeba stać najwyżej 8 bloków od wyrwy, gracza albo wyznaczonego miejsca.",
      places: [
        { id: "Rift", name: "Przy wyrwie", text: "Stań przy aktywnej wyrwie temporalnej. Tak jest domyślnie.", video: "W15" },
        { id: "NearPlayer", name: "Przy graczu", text: "Stań obok przyjaciela, który czeka na powrót. Musi być online. Bana tak nie zdejmiesz.", video: "W16" },
        { id: "FixedPlace", name: "W wyznaczonym miejscu", text: "Stań przy miejscu, które zaznaczył admin, na przykład przy ołtarzu w osadzie." },
        { id: "Anywhere", name: "Wszędzie", text: "Wybierz gracza komendą <code>/rebirth target &lt;gracz&gt;</code>. Potem użyj zębatki, gdziekolwiek jesteś.", video: "W17" },
      ],
      appearTitle: "Gdzie pojawia się wskrzeszony gracz",
      appear: "Domyślnie przyjaciel pojawia się dwa bloki od ciebie, niezależnie od tego, gdzie był. Serwer może to zmienić. Wtedy przyjaciel wstaje tam, gdzie jest, w miejscu śmierci albo w punkcie odrodzenia. Serwer może też ustawić przerwę, zanim tę samą osobę da się znowu wskrzesić.",
    },

    faq: {
      title: "Częste pytania",
      players: "Gracze", owners: "Właściciele serwerów",
      list: [
        { who: "players", q: "Wylogowałem się jako duch. Czy czas stoi?",
          a: "Nie. Liczy się czas rzeczywisty. Płynie, gdy jesteś offline, a nawet gdy serwer jest wyłączony. Jeśli czas minął, wracasz zaraz po wejściu na serwer." },
        { who: "players", q: "Co się dzieje z ekwipunkiem po ostatniej śmierci?",
          a: "Mod go nie rusza. Obowiązują zwykłe zasady świata. Tak samo jest przy śmierci na koniec wygnania." },
        { who: "players", q: "Rytuał nie zadziałał.",
          a: "Sprawdź, czy:<br>– żyjesz, nie jesteś powalony i masz życia,<br>– trzymasz zębatkę temporalną pełne 3,5 sekundy,<br>– stoisz tam, gdzie na serwerze działa rytuał,<br>– ktoś czeka na powrót (<code>/rebirth ghosts</code>),<br>– wybrałeś gracza komendą <code>/rebirth target &lt;gracz&gt;</code>, jeśli czeka kilka osób albo rytuał działa wszędzie,<br>– minęła przerwa przed kolejnym rytuałem dla tej osoby." },
        { who: "players", q: "Nie widzę ducha przyjaciela.",
          a: "Domyślnie duch wygląda jak ognik. Echo z nickiem widzi tylko gracz z zębatką temporalną w ręce. Serwer może też ukryć duchy, a obserwatora nie widzi nikt." },
        { who: "players", q: "Nie widzę licznika.",
          a: "Licznika nie ma, gdy serwer go wyłączył, gdy czas nie ma limitu i przy banie. Przy banie czas powrotu podaje wiadomość, którą dostajesz przy rozłączeniu." },
        { who: "players", q: "Czy powalenie z moda Downed zabiera życie?",
          a: "Zależy od serwera. Domyślnie życie zabiera tylko śmierć. Serwer z modem Downed może liczyć każde powalenie jako utratę życia. Może też ustawić, że śmierć zabiera wtedy od razu wszystkie życia." },
        { who: "owners", q: "Mod nic nie robi.",
          a: "Prawie na pewno świat ma nieograniczoną liczbę żyć. Wpisz <code>/worldconfig playerlives 3</code> i zrestartuj serwer. Jeśli w logu jest „Lives, ghosts and rituals are DISABLED”, mod nie obsługuje jeszcze twojej wersji gry. Trzeba poczekać na aktualizację." },
        { who: "owners", q: "Zmieniłem preset, a gracz dalej jest duchem.",
          a: "Forma, miejsce, czas i powrót zapisują się w chwili śmierci. Nowe ustawienia dotyczą dopiero kolejnych śmierci. Żeby skończyć czyjąś karę, użyj <code>/rebirth revive &lt;gracz&gt;</code>." },
        { who: "owners", q: "Czy rytuału da się użyć jako teleportu do znajomego?",
          a: "Tak. Domyślnie wskrzeszony pojawia się obok gracza, który odprawił rytuał. Jeśli tego nie chcesz, zmień w konfiguratorze miejsce, w którym się pojawia, albo ustaw przerwę między rytuałami." },
        { who: "owners", q: "Gracze wylogowują się i tak przeczekują karę.",
          a: "Czas płynie offline w każdej formie. Jeśli kara ma coś kosztować, wybierz wygnanie albo bana. Na wygnaniu gracz gra dalej, ale daleko od bazy. Z banem nie gra w ogóle." },
        { who: "owners", q: "Czy gracze muszą coś instalować?",
          a: "Nie. Mod musi być i na serwerze, i u gracza, ale gra przy wejściu sama proponuje pobranie modów serwera z Mod DB. Wersję spoza Mod DB, na przykład testową, trzeba dać graczom ręcznie." },
      ],
    },

    footer: {
      tagline: "Temporal Rebirth to mod Mrucznika do Vintage Story. Nie jest powiązany z Anego Studios.",
      links: { moddb: "Mod DB", github: "GitHub", issues: "Zgłoś błąd", wiki: "Wiki", config: "Konfigurator" },
      version: "Strona opisuje wersję 0.9 dla Vintage Story 1.22.7.",
    },
    noscript: "Ta strona wymaga JavaScriptu. Te same informacje znajdziesz w wiki moda.",
    wikiLink: (page, label) => wiki(page, label),
  };
})();
