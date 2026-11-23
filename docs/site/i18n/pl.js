// Polish texts of the mod's page. Another language is another file like this one, loaded by index.html
// (see docs/site/i18n.js). Strings may hold simple HTML (<code>, <b>, <a>); functions build sentences from settings.
(function () {
  const W = "https://github.com/Mrucznik/vs-temporal-rebirth/wiki/";
  const wiki = (page, label) => `<a href="${W}${page}" target="_blank" rel="noopener">${label}</a>`;
  const few = (n, a, b) => {
    const t = n % 10, h = n % 100;
    return Number.isInteger(n) && t >= 2 && t <= 4 && (h < 12 || h > 14) ? a : b;
  };
  const blocks = (n) => n + (n === 1 ? " bloku" : " bloków");

  (window.TR_SITE ??= {}).pl = {
    name: "Polski",
    meta: {
      title: "Temporal Rebirth – mod do Vintage Story",
      description: "Mod do Vintage Story: zębatki temporalne dają dodatkowe życia, a po stracie ostatniego możesz wrócić do gry.",
    },
    nav: {
      skip: "Przejdź do treści", home: "Strona", config: "Konfigurator", wiki: "Wiki", menu: "Spis treści", language: "Język",
      sections: { how: "Jak to działa", presets: "Presety", forms: "Formy", rituals: "Rytuały", faq: "FAQ" },
    },
    hero: {
      kicker: "Mod do Vintage Story 1.22.7",
      pitch: "Zbieraj życia zębatkami.<br>Po ostatniej śmierci możesz <em>wrócić</em>.",
      lead: "Na serwerze z limitem żyć ostatnia śmierć zwykle kończy grę na dobre. Z tym modem zębatki temporalne dają dodatkowe życia. Gdy stracisz ostatnie, przez jakiś czas jesteś duchem, zombie albo wygnańcem. Wracasz, gdy minie czas albo gdy przyjaciel odprawi rytuał.",
      download: "Pobierz z Mod DB", github: "Wydania na GitHubie", config: "Konfigurator", how: "Jak to działa",
      video: "Ostatnia śmierć: ognik, przyjaciel bierze zębatkę, echo, rytuał i powrót.",
      facts: ["Serwery i gra jednoosobowa", "6 gotowych presetów", "Działa z modem Downed"],
    },
    video: { enlarge: "Powiększ film", close: "Zamknij", play: "Odtwórz", unsupported: "Twoja przeglądarka nie odtwarza tego filmu." },

    how: {
      title: "Co się dzieje, gdy umierasz",
      lead: "Cała droga od pierwszego życia do powrotu. Kliknij krok, żeby go zobaczyć. Tak wygląda domyślne ustawienie; serwer może zmienić szczegóły.",
      or: "albo",
      steps: [
        { short: "Życia z zębatek", title: "Zbierasz życia", video: "M3",
          caption: "Zębatka daje życie, a ekran śmierci pokazuje, ile zostało.",
          text: "Na start masz tyle żyć, ile ustawi serwer. Każda <b>zębatka temporalna</b> daje kolejne: przytrzymaj ją prawym przyciskiem na bloku przez 3,5 sekundy. Zębatka przepada, punkt odrodzenia przenosi się w to miejsce, a ty dostajesz życie.",
          more: "Ile żyć ci zostało, pokazuje ekran śmierci i komenda <code>/rebirth lives</code>. Serwer może ustawić, ile żyć daje jedna zębatka i ile najwyżej da się z nich uzbierać." },
        { short: "Ostatnia śmierć", title: "Tracisz ostatnie życie", video: "W4",
          caption: "Ognik ducha, widziany przez innego gracza.",
          text: "Dopóki masz życia, odradzasz się jak zwykle. Gdy stracisz ostatnie, po chwili nie wracasz do gry, tylko stajesz się kimś innym. Domyślnie <b>duchem</b>: inni widzą w miejscu twojej śmierci unoszący się ognik.",
          more: "Wszyscy na serwerze dostają wiadomość, że straciłeś ostatnie życie. Co dzieje się z ekwipunkiem, decydują zwykłe zasady świata." },
        { short: "Duch, zombie…", title: "Zostajesz duchem", video: "W6",
          caption: "Oczami ducha: lot, ściana i bariera na granicy.",
          text: "Duch lata i patrzy, ale niczego nie dotknie. Musisz zostać w swoim miejscu, domyślnie do 24 bloków od miejsca śmierci. Na granicy zatrzymuje cię <b>bariera</b>: z daleka prawie niewidoczna, z bliska rdzawa ściana z obracającymi się zębatkami.",
          more: "Serwer może zamiast ducha zrobić z ciebie obserwatora, zombie, wygnańca albo dać ci bana na jakiś czas. Zobacz niżej: <a href=\"#formy\">formy</a>." },
        { short: "Czekasz", title: "Czekasz, aż minie czas", video: "W14",
          caption: "Ostatnie sekundy licznika.",
          text: "Licznik u góry ekranu pokazuje, ile zostało. Domyślnie wracasz następnego dnia o 06:00 czasu serwera. To czas rzeczywisty: płynie też wtedy, gdy wylogujesz się z gry.",
          more: "Kolejna śmierć w tym stanie niczego nie zmienia: nie kosztuje życia, nie skraca ani nie wydłuża czekania." },
        { short: "Albo rytuał", title: "Albo przyjaciel odprawia rytuał", video: "W15", alt: true,
          caption: "Rytuał przy wyrwie temporalnej.",
          text: "Żywy przyjaciel może sprowadzić cię wcześniej. Staje przy aktywnej <b>wyrwie temporalnej</b>, trzyma zębatkę temporalną i przytrzymuje prawy przycisk przez 3,5 sekundy. Zębatka przepada, a ty wracasz.",
          more: "Żeby wiedział, kogo wskrzesza, przyjaciel z zębatką w ręce widzi zamiast ognika <b>echo</b>: falującą sylwetkę z twoim nickiem. Więcej w części <a href=\"#rytualy\">rytuały</a>." },
        { short: "Powrót", title: "Wracasz do gry", video: "W10",
          caption: "Licznik dobiega końca i gracz wraca.",
          text: "Wracasz z jednym życiem, domyślnie w swoim punkcie odrodzenia. Wszyscy na serwerze dostają wiadomość, że wróciłeś. Znów możesz zbierać zębatki.",
          more: "Jeśli byłeś offline, wracasz przy najbliższym wejściu na serwer. Admin może też przywrócić cię wcześniej komendą." },
      ],
    },

    presets: {
      title: "Presety",
      lead: "Serwer wybiera jeden z gotowych presetów albo układa własny w konfiguratorze. Wybierz preset i zobacz, co czeka gracza.",
      rows: { form: "Kim się stajesz", where: "Gdzie", time: "Na jak długo", back: "Jak wracasz", ritual: "Rytuał" },
      configure: "Ustaw ten preset w konfiguratorze",
      videos: { Classic: "W4", Rescue: "W17", Undead: "M2-zombie", Exile: "M2-exile", Timeout: "W11", Hardcore: "M2-ban" },
      captions: {
        Classic: "Ostatnia śmierć i ognik ducha w miejscu śmierci.", Rescue: "Rytuał w dowolnym miejscu, po wybraniu gracza.",
        Undead: "Zombie: zabarwione ciało i poświata.", Exile: "Wygnanie: zwykła gra, ale daleko i na czas.",
        Timeout: "Rytuał przy wyrwie zdejmuje bana.", Hardcore: "Ban: serwer rozłącza gracza.",
      },
      // Sentences from the preset's values (schema.js). s = all settings after the preset.
      form: (s) => ({
        Ghost: { Nobody: "Duch, niewidoczny dla innych", Wisp: "Duch, inni widzą ognik", WispAndEcho: "Duch: inni widzą ognik, a z zębatką w ręce twoje echo" }[s["Ghost.Visible"]],
        Spectator: "Obserwator: lata przez ściany, niewidoczny",
        Zombie: s["Zombie.CanUseItems"] ? "Zombie, może używać przedmiotów" : "Zombie bez przedmiotów: walczy gołymi rękami, nie otworzy skrzyni",
        Player: "Zwykły gracz, ale na wygnaniu",
        Ban: "Ban: nie wejdziesz na serwer",
      })[s.OutOfLives],
      where: (s) => {
        if (s.OutOfLives === "Ban") return "Poza serwerem";
        const at = { Body: "Miejsce śmierci", Spawn: "Punkt odrodzenia", Fixed: "Miejsce wyznaczone przez admina", Zone: "Strefa z land claimów" }[s["Place.At"]];
        if (s["Place.At"] === "Zone") return at;
        return at + (s["Place.Radius"] === 0 ? ", bez limitu odległości" : ", do " + blocks(s["Place.Radius"]));
      },
      time: (d) => d.kind === "forever" ? "Bez limitu: aż ktoś cię wskrzesi"
        : d.kind === "next" ? "Do następnego dnia, " + d.time + " czasu serwera"
        : d.kind === "h" ? d.n + " " + (d.n === 1 ? "godzina" : few(d.n, "godziny", "godzin")) + " czasu rzeczywistego"
        : d.n + " " + (d.n === 1 ? "dzień" : "dni") + " czasu rzeczywistego",
      back: (s, forever) => {
        const at = { Spawn: "w punkcie odrodzenia", Body: "w miejscu śmierci", Fixed: "w wyznaczonym miejscu" }[s["Return.At"]];
        const lives = s["Return.Lives"] === -1 ? "z pełną pulą żyć" : s["Return.Lives"] === 1 ? "z 1 życiem" : "z " + s["Return.Lives"] + " życiami";
        if (forever) return "Tylko przez rytuał albo admina, " + lives + ".";
        return (s["Return.DieAtEnd"] ? "Na koniec giniesz (ta śmierć nie kosztuje życia). " : "") + "Gdy minie czas, wracasz " + at + ", " + lives + ".";
      },
      ritual: (s) => {
        const w = s["Ritual.Where"];
        if (w === "Disabled") return "Brak: nikt cię nie wskrzesi";
        const ban = s.OutOfLives === "Ban";
        if (ban && (w === "NearPlayer" || !s["Ritual.CanLiftBan"])) return "Nie zdejmuje bana";
        const where = { Rift: "Przy aktywnej wyrwie", NearPlayer: "Obok ciebie, gdy jesteś online", FixedPlace: "W miejscu wyznaczonym przez admina", Anywhere: "Wszędzie, po wybraniu cię komendą <code>/rebirth target</code>" }[w];
        const at = ban ? "ban znika, wracasz przy następnym wejściu" : { Caster: "pojawiasz się obok wskrzeszającego", InPlace: "wstajesz tam, gdzie jesteś", Body: "wracasz w miejscu śmierci", Spawn: "wracasz w punkcie odrodzenia" }[s["Ritual.ReviveAt"]];
        return where + "; " + at + ".";
      },
    },

    forms: {
      title: "Kim się stajesz",
      lead: "Formę wybiera serwer. W każdej obowiązuje to samo: nie opuścisz swojego miejsca, a kolejna śmierć nie zabiera życia i nie skraca kary.",
      more: "Więcej w wiki",
      list: [
        { id: "Ghost", name: "Duch", tag: "domyślnie", wiki: "Ghost",
          text: ["Latasz i patrzysz, ale niczego nie dotkniesz, a ściany cię zatrzymują. Nie odejdziesz dalej niż pozwala bariera.",
            "Inni widzą w tym miejscu <b>ognik</b>: małe, ciemne światełko z żarem w środku. Nie widać, czyj jest. Kto trzyma zębatkę temporalną, widzi <b>echo</b>: twoją sylwetkę z nickiem, żeby wiedział, kogo wskrzesić."],
          media: [{ v: "W5", label: "Oczami innych", caption: "Ognik zmienia się w echo, gdy gracz bierze zębatkę do ręki." },
            { v: "W6", label: "Oczami ducha", caption: "Lot, ściana i bariera na granicy." }] },
        { id: "Spectator", name: "Obserwator", wiki: "Spectator",
          text: ["Jak duch, ale przelatujesz przez ściany. Niczego nie dotkniesz i nie opuścisz swojego miejsca.",
            "Nikt cię nie widzi. Kto czeka na powrót, sprawdzisz komendą <code>/rebirth ghosts</code>."],
          media: [{ v: "W7", caption: "Obserwator przelatuje przez ściany." }] },
        { id: "Zombie", name: "Zombie", wiki: "Zombie",
          text: ["Chodzisz po świecie, walczysz i obrywasz, ale tylko gołymi rękami. Domyślnie nie użyjesz przedmiotów, nie podniesiesz niczego z ziemi i nie otworzysz skrzyni ani drzwi.",
            "Wszyscy widzą zabarwione ciało i poświatę. Gdy zginiesz, wstajesz znowu jako zombie, a przez 5 sekund nic cię nie zrani."],
          media: [{ v: "W8", caption: "Zombie z poświatą nie otworzy skrzyni." }] },
        { id: "Exile", name: "Wygnaniec", wiki: "Exile",
          text: ["Grasz normalnie, ale w miejscu wyznaczonym przez admina, zwykle daleko od bazy, i tylko przez określony czas. Granicę pilnuje bariera.",
            "W presecie Exile po 24 godzinach giniesz (ta śmierć nie kosztuje życia) i wracasz do punktu odrodzenia z pełną pulą żyć."],
          media: [{ v: "M4", caption: "Wygnanie: granica, bariera i licznik." },
            { v: "W9", label: "Śmierć na wygnaniu", caption: "Śmierć na wygnaniu nie uwalnia: wstajesz na miejscu, licznik leci dalej." }] },
        { id: "Ban", name: "Ban", wiki: "Ban",
          text: ["Serwer rozłącza cię chwilę po śmierci i mówi, kiedy możesz wrócić. Do tego czasu nie wejdziesz.",
            "Na niektórych serwerach przyjaciel może zdjąć bana rytuałem, nawet gdy jesteś offline. Wracasz przy następnym wejściu."],
          media: [{ v: "M2-ban", caption: "Ban: serwer rozłącza gracza i podaje czas powrotu." },
            { v: "W11", label: "Rytuał zdejmuje bana", caption: "Przyjaciel zdejmuje bana rytuałem przy wyrwie." }] },
      ],
    },

    rituals: {
      title: "Jak sprowadzić przyjaciela",
      lead: "Rytuał wskrzesza kogoś, komu skończyły się życia, zanim minie jego czas. Potrzebujesz jednej zębatki temporalnej i musisz sam żyć: duchy, zombie i wygnańcy rytuału nie odprawią.",
      steps: [
        "Sprawdź, kto czeka: <code>/rebirth ghosts</code>. Jeśli czeka więcej osób, wybierz jedną: <code>/rebirth target &lt;gracz&gt;</code>.",
        "Idź tam, gdzie na twoim serwerze działają rytuały (domyślnie: do 8 bloków od aktywnej wyrwy).",
        "Weź zębatkę temporalną do ręki. Duch pokaże się wtedy jako echo z nickiem.",
        "Przytrzymaj prawy przycisk na bloku przez 3,5 sekundy. Zębatka przepada, przyjaciel wraca, a wszyscy dostają wiadomość.",
      ],
      fail: "Nie wyszło? Zębatka zostaje w ręce, gdy nikt nie czeka, trzeba najpierw wybrać gracza albo trwa jeszcze przerwa między rytuałami. Jeśli zębatka tylko ustawiła punkt odrodzenia, w pobliżu nie ma miejsca rytuału.",
      whereTitle: "Gdzie działa rytuał",
      whereLead: "Wybiera serwer. „Blisko” to domyślnie 8 bloków.",
      places: [
        { id: "Rift", name: "Przy wyrwie", text: "Stań przy aktywnej wyrwie temporalnej. Domyślne ustawienie.", video: "W15" },
        { id: "NearPlayer", name: "Przy graczu", text: "Stań obok przyjaciela. Musi być online. Bana tak nie zdejmiesz.", video: "W16" },
        { id: "FixedPlace", name: "W wyznaczonym miejscu", text: "Stań przy miejscu, które zaznaczył admin, na przykład przy ołtarzu w osadzie." },
        { id: "Anywhere", name: "Wszędzie", text: "Najpierw <code>/rebirth target &lt;gracz&gt;</code>, potem zębatka, gdziekolwiek jesteś.", video: "W17" },
      ],
      appearTitle: "Gdzie pojawia się wskrzeszony",
      appear: "Domyślnie obok wskrzeszającego, dwa bloki od niego, więc rytuał ściąga przyjaciela do ciebie. Serwer może zamiast tego zostawić go tam, gdzie jest, albo przenieść do miejsca śmierci lub punktu odrodzenia. Może też ustawić przerwę, zanim tę samą osobę da się wskrzesić znowu.",
    },

    faq: {
      title: "Pytania",
      players: "Gracze", owners: "Właściciele serwerów",
      list: [
        { who: "players", q: "Wylogowałem się jako duch. Czas stoi?",
          a: "Nie. To czas rzeczywisty: płynie, gdy jesteś offline i gdy serwer jest wyłączony. Jeśli minął, zanim wrócisz, wracasz zaraz po wejściu." },
        { who: "players", q: "Co z moim ekwipunkiem po ostatniej śmierci?",
          a: "Mod go nie rusza. Działają zwykłe zasady świata, także przy śmierci na koniec wygnania." },
        { who: "players", q: "Mój rytuał nic nie zrobił.",
          a: "Sprawdź po kolei: żyjesz, nie jesteś powalony i masz życia; trzymasz zębatkę temporalną pełne 3,5 sekundy; stoisz tam, gdzie na serwerze działają rytuały; ktoś czeka (<code>/rebirth ghosts</code>); przy kilku czekających albo rytuale „wszędzie” najpierw <code>/rebirth target &lt;gracz&gt;</code>; minęła przerwa dla tej osoby." },
        { who: "players", q: "Nie widzę ducha przyjaciela.",
          a: "Domyślnie widać ognik. Echo z nickiem widzi tylko ktoś z zębatką temporalną w ręce. Serwer może duchy ukryć, a obserwatorów nie widzi nikt." },
        { who: "players", q: "Nie widzę licznika.",
          a: "Serwer wyłączył licznik, czas nie ma końca albo masz bana. Przy banie czas powrotu podaje wiadomość przy rozłączeniu." },
        { who: "players", q: "Czy powalenie (Downed) zabiera życie?",
          a: "Zależy od serwera. Domyślnie liczy się tylko śmierć. Serwer z modem Downed może liczyć każde powalenie jako życie albo zabierać wszystkie życia przy śmierci." },
        { who: "owners", q: "Mod nic nie robi.",
          a: "Prawie na pewno świat ma nieograniczone życia. Wpisz <code>/worldconfig playerlives 3</code> i zrestartuj serwer. Jeśli w logu jest „Lives, ghosts and rituals are DISABLED”, mod czeka na aktualizację do twojej wersji gry." },
        { who: "owners", q: "Zmieniłem preset, a gracz dalej jest duchem.",
          a: "Forma, miejsce, czas i powrót zapisują się w chwili śmierci. Zmiana dotyczy kolejnych śmierci. Żeby zakończyć czyjąś karę, użyj <code>/rebirth revive &lt;gracz&gt;</code>." },
        { who: "owners", q: "Jak przetestować moda bez tracenia postaci?",
          a: "Na zapasowym koncie: <code>/rebirth setlives &lt;gracz&gt; 0</code> sprawia, że następna śmierć jest ostatnia." },
        { who: "owners", q: "Czy rytuał może służyć za teleport do znajomego?",
          a: "Tak, przy domyślnym „obok wskrzeszającego”. Jeśli nie chcesz, ustaw w konfiguratorze inne miejsce pojawienia się albo przerwę między rytuałami." },
        { who: "owners", q: "Gracze przeczekują karę, wylogowując się.",
          a: "Czas płynie offline we wszystkich formach. Jeśli kara ma boleć, użyj wygnania (gra trwa, ale daleko) albo bana." },
        { who: "owners", q: "Czy gracze muszą coś instalować?",
          a: "Nie. Mod jest potrzebny po obu stronach, ale gra sama proponuje pobranie modów serwera z Mod DB przy wejściu. Wersję spoza Mod DB (np. testową) trzeba skopiować graczom ręcznie." },
      ],
    },

    footer: {
      tagline: "Temporal Rebirth to mod do Vintage Story autorstwa Mrucznika. Nie jest powiązany z Anego Studios.",
      links: { moddb: "Mod DB", github: "GitHub", issues: "Zgłoś błąd", wiki: "Wiki", config: "Konfigurator" },
      version: "Strona opisuje wersję 0.9 dla Vintage Story 1.22.7.",
    },
    noscript: "Ta strona potrzebuje JavaScriptu. Wszystko, co tu jest, znajdziesz też w wiki moda.",
    wikiLink: (page, label) => wiki(page, label),
  };
})();
