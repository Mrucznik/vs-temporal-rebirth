// Polish. Setting names and help live in schema.js; here are the page, the summary and the file comments.
(function () {
  const few = (n, a, b) => {
    const t = n % 10, h = n % 100;
    return Number.isInteger(n) && t >= 2 && t <= 4 && (h < 12 || h > 14) ? a : b;
  };
  const blocks = (n) => n + (n === 1 ? " bloku" : " bloków");
  const lives = (n) => n === -1 ? "z pełną pulą żyć" : n === 1 ? "z 1 życiem" : "z " + n + " życiami";

  (window.TR_I18N ??= {}).pl = {
    name: "Polski",
    ui: {
      navHome: "Strona", navConfig: "Konfigurator", navWiki: "Wiki",
      lead: "Konfigurator moda Temporal Rebirth do Vintage Story. Ustawisz tu, co zabiera życia, kim staje się gracz po stracie ostatniego, gdzie i jak długo wtedy przebywa, jak wraca i czy inni mogą go wskrzesić. Na końcu pobierzesz gotowy plik na serwer.",
      downedLabel: "Na serwerze jest mod Downed", downedHelp: "Odblokowuje ustawienia, w których życia zabiera powalenie.", downedLink: "Strona moda Downed", downedNeeded: "Potrzebny mod Downed. Włącz go u góry strony.",
      tabSettings: "Ustawienia", tabMessages: (n) => n ? "Komunikaty (własne: " + n + ")" : "Komunikaty", vars: "Można użyć: ", builtin: "tekst moda",
      wiki: "Wiki", wikiTitle: "Otwiera stronę w wiki (po angielsku)",
      pick: "Wybierz styl", tune: "Dopasuj szczegóły", story: "Co czeka gracza", install: "Wgraj na serwer",
      importOpen: "Wczytaj moje ustawienia", importLabel: "Wklej plik temporalrebirth.json (także z moda 0.6.x), komendę importu albo link.",
      importGo: "Wczytaj", importFile: "Wybierz plik", showFile: "Pokaż plik", withComments: "Z objaśnieniami",
      changes: (n) => n === 1 ? "1 zmiana" : n + " " + few(n, "zmiany", "zmian"), reset: (v) => "Przywróć: " + v, on: "włączone", off: "wyłączone",
      hours: "Godziny", days: "Dni", next: "Do następnego dnia", forever: "Bez limitu",
      allowed: "Dozwolone: ",
      died: "Koniec żyć", back: "Powrót", never: "Bez powrotu",
      copy: "Kopiuj", copied: "Skopiowano", copyFail: "Zaznacz tekst i skopiuj ręcznie", downloaded: "Pobrano plik", loaded: "Wczytano ustawienia",
      download: "Pobierz temporalrebirth.json", copyLink: "Kopiuj link do ustawień",
      s1: "Włącz limit żyć w świecie i zrestartuj serwer. Bez limitu mod nic nie robi.", startLives: "Życia na start", startLivesHelp: "To ustawienie świata, nie trafia do pliku. Komenda jest niżej, w „Wgraj na serwer”.", startLivesError: "Dozwolone: 1 … 1000",
      s2: "Wgraj plik do folderu <code>ModConfig</code> i zrestartuj serwer.", s2or: "Albo wklej w grze jako admin, bez restartu:",
      s2admin: "Komenda nie zmienia sekcji Administracja, robi to tylko plik.", s2blocked: "Komenda nie zadziała, jeśli zmiana ustawień jest już na serwerze wyłączona.",
      s3: "Zaznacz miejsca. Stań tam, gdzie mają być, i wpisz:", pstart: "Gdzie gracz przebywa.", pstartZone: "Gdzie gracz się pojawia. Musi leżeć w strefie.", preturn: "Gdzie gracz wraca.", pritual: "Gdzie działa rytuał.",
      sClaims: "Wyznacz strefę land claimami:",
      claimNotes: ["Tylko za pierwszym razem.", "", "Stojąc w jednym rogu strefy.", "Stojąc w przeciwległym rogu.", "Cała wysokość, inaczej granica jest też pod ziemią.", "", "Dodaje claim do strefy.", ""],
      claimMore: "Kolejny claim: to samo bez pierwszej komendy. Rozmiar ogranicza twoja rola na serwerze.",
      rTitle: "Uprawnienia dla moderatorów",
      rIntro: "Admini i gracze z controlserver mogą robić wszystko, co włączone wyżej. Innym nadasz wybrane uprawnienia komendą w grze, po instalacji moda.",
      rWho: "Komu", rRole: "Rola", rPlayer: "Gracz", rRoles: "Np. sumod (moderator survivalu), crmod (moderator creative) albo własna.",
      rNameRole: "Wpisz nazwę roli, bez spacji.", rNamePlayer: "Wpisz nick gracza.",
      rRevoke: "Cofniesz to tą samą komendą z revoke zamiast grant.", rOff: "Wszystkie akcje są wyłączone, nie ma czego nadać.",
      s4: "Sprawdź wynik.",
      repOld: "Przeliczono ustawienia z moda 0.6.x.", repOk: "Odczytano wszystko.", repProblems: "Odczytano, z wyjątkiem:",
      unknown: (k) => k + ": nieznane ustawienie, pominięte", badvalue: (k, a) => k + ": zła wartość, została domyślna. Dozwolone: " + a, newer: "Plik pochodzi z nowszej wersji, części ustawień może brakować.",
      empty: "Najpierw coś wklej.", json: (d) => "To nie jest poprawny JSON. " + d, code: "Nie da się odczytać tego kodu ani linku. Sprawdź, czy skopiowano całość.",
      w: {
        onlyRitual: "Czas jest bez limitu, więc gracz wróci dopiero, gdy ktoś odprawi rytuał albo przywróci go admin.",
        never: "Czas jest bez limitu i żaden rytuał nie pomoże, więc gracza przywróci tylko admin.",
        onlyRitualNoAdmin: "Czas jest bez limitu, a admini nie mogą przywracać graczy. Zostaje rytuał albo konsola serwera.",
        neverNoAdmin: "Czas jest bez limitu, rytuał nie pomoże, a admini nie mogą przywracać graczy. Zostaje konsola serwera.",
        banNearPlayer: "Zbanowanego nie ma na serwerze, więc rytuał przy graczu nie zadziała. Wybierz inne miejsce rytuału.",
        pointsOff: "Te ustawienia wymagają zaznaczenia miejsca w grze, a masz to wyłączone w Administracji. Zaznacz je, zanim wgrasz plik.",
        setLivesRevives: "Admini nie mogą przywracać graczy przez /rebirth revive, ale mogą przez /rebirth setlives.",
        livesOverMax: "Gracz wraca z większą liczbą żyć niż limit, więc zębatki nic mu nie dadzą.",
        noPenalty: "Zwykły gracz bez limitu odległości prawie nie odczuje straty żyć. Ustaw odległość, wyznaczone miejsce albo strefę.",
        zoneClaims: "Strefa zadziała dopiero, gdy udostępnisz grupie claimy. Bez nich nikogo nie zatrzyma.",
        walkAway: "Bez limitu odległości gracz może odejść z wyznaczonego miejsca.",
        noTime: "W twojej wiadomości o banie nie ma {time}, więc gracz nie dowie się, kiedy wróci.",
      },
    },
    file: {
      preset: "Gotowy preset: ", presetHow: "Wpisz nazwę i zrestartuj serwer. Mod wypełni resztę i wyczyści to pole.",
      vars: "Można użyć: ", empty: "Puste = tekst moda.", privilege: "Uprawnienie: ",
      lower: (s) => s.toLowerCase(), noBraces: "tekst bez { }", oneWord: (n) => "jedno słowo, do " + n + " znaków", text: (n) => "tekst do " + n + " znaków",
    },
    story: {
      rows: { loses: "Życie traci", state: "Stan", where: "Gdzie", time: "Na ile", dies: "Gdy zginie", back: "Powrót", ritual: "Rytuał", alone: "Sam" },
      count: { Downs: "Przy powaleniu. Niepodniesiony odradza się.", DownsFinalDeath: "Przy powaleniu. Śmierć zabiera wszystkie życia." },
      banned: "Nie wejdzie na serwer",
      ghost: { Nobody: ", niewidoczny", Wisp: ", widoczny jako ognik", WispAndEcho: ", widoczny jako ognik, a z zębatką jako sylwetka" },
      items: (can) => can ? ", może używać przedmiotów" : ", nie używa przedmiotów",
      zone: (group) => "Strefa grupy „" + group + "”",
      access: { Traverse: ", niczego nie użyje", Use: ", używa bloków, ale nie buduje", All: ", może budować i niszczyć" },
      radius: (n) => n === 0 ? ", bez limitu odległości" : ", do " + blocks(n),
      duration: (d) => d.kind === "forever" ? "bez limitu czasu" : d.kind === "next" ? "do następnego dnia, " + d.time
        : d.kind === "h" ? d.n + " " + (d.n === 1 ? "godzina" : Number.isInteger(d.n) ? few(d.n, "godziny", "godzin") : "godziny")
        : d.n + " " + (d.n === 1 ? "dzień" : Number.isInteger(d.n) ? "dni" : "dnia"),
      timer: ", licznik na ekranie",
      dies: "Wraca w to samo miejsce, a czas leci dalej. Zębatki nic mu nie dają.",
      back: { Spawn: "W punkcie odrodzenia", Body: "W miejscu śmierci", Fixed: "W wyznaczonym miejscu" },
      lives,
      firstDies: "Najpierw ginie. ", nextLogin: " Przy następnym wejściu.", offline: " Jeśli jest offline, przy następnym wejściu.",
      onlyRitual: (l) => "Tylko przez rytuał, " + l + ".", none: "Brak.",
      health: (n) => " Zdrowie " + n + "%.", satiety: (n) => " Sytość " + n + "%.",
      ritualOff: "Wyłączony", noLift: "Nie zdejmuje bana",
      ritualAt: { Rift: (n) => "Do " + blocks(n) + " od wyrwy", NearPlayer: (n) => "Do " + blocks(n) + " od wskrzeszanego", FixedPlace: (n) => "Do " + blocks(n) + " od wyznaczonego miejsca", Anywhere: () => "Wszędzie" },
      appears: (at, ban) => "Gracz pojawia się " + { Caster: "obok wskrzeszającego", InPlace: ban ? "tam, gdzie był ostatnio" : "tam, gdzie jest", Body: "w miejscu śmierci", Spawn: "w punkcie odrodzenia" }[at] + (ban ? " przy następnym wejściu" : "") + ".",
      cooldown: (n) => " Ponownie po " + n + " godz.",
      healthCost: (n, min) => " Przy wyrwie także bez zębatki: wskrzeszony traci na stałe " + n + " zdrowia (nie poniżej " + min + "), zębatka ofiarowana wyrwie je oddaje.",
      selfAtRift: (n) => "Duch wraca sam, gdy zawiśnie w wyrwie" + (n > 0 ? ", tracąc " + n + " zdrowia." : "."),
      nowMessages: (n) => n ? "Własne: " + n : "Wszystkie z moda",
      nowLives: (start, gear, max) => start + " na start, zębatka: " + (gear === -1 ? "według świata" : "+" + gear) + ", " + (max === -1 ? "bez limitu" : "limit " + max),
      nowAdmin: (off) => off.length ? "Wyłączone: " + off.join(", ") : "Wszystko dozwolone",
    },
  };
})();
