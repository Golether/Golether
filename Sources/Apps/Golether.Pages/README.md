# Golether.Pages

The project page: <https://golether.github.io/>. Plain static files with no build step. On every change in the `main`
branch `.github/workflows/pages.yml` copies this folder into the repository `Golether/golether.github.io`, whose GitHub
Pages serve the root of the organization's site.

| File | What for |
|---|---|
| `index.html`, `assets/site.css`, `assets/site.js` | The card for people without technical knowledge: what it is, how to join by invitation, how to invite, where to download. |
| `join/index.html`, `assets/join.js` | The invitation page: turns `https://…/join/#<invitation>` into `golether://join/v1/<invitation>` and hands it to the application. |
| `assets/languages.js`, `assets/flags/*.svg` | The languages of both pages and the language picker with flags; the links to the documentation in the chosen language. |
| `updates.json` | The latest release. The "Download" buttons of the page and the update check of the application both read it. |
| `config.json` | The addresses of the project's rendezvous server and of its signalling, sealed. The application reads it in the "Automatically" mode, the browser version at start. |

## First publication

1. Create the public repository `Golether/golether.github.io` with **Add a README file** ticked, so that it has a
   `main` branch (the first publication replaces the README). In its **Settings → Pages** keep **Deploy from a branch**,
   `main`, `/ (root)`.
2. Make a key pair for it: `ssh-keygen -t ed25519 -C "golether pages" -f site-deploy-key`, and press Enter twice
   at the passphrase prompts (the workflow cannot type one).
3. In `Golether/golether.github.io`: **Settings → Deploy keys → Add deploy key**, the content of
   `site-deploy-key.pub`, **Allow write access** ticked. The key writes to this one repository only.
4. In `Golether/Golether`: **Settings → Secrets and variables → Actions → New repository secret** named
   `SITE_DEPLOY_KEY`, the content of `site-deploy-key`. Delete both key files afterwards; a new pair replaces a lost
   one.
5. Run the **Pages** workflow by hand on the **Actions** tab (later every change of this folder in `main` runs it).
6. A couple of minutes later the page opens at <https://golether.github.io/>.

## A new release

1. Push a version tag: `git tag 0.3.0; git push origin 0.3.0`. The **Packages** workflow builds
   `Golether-0.3.0-osx-arm64.tar.gz` and `Golether-0.3.0-osx-x64.tar.gz` and attaches them to the release (to a draft
   when there is no release yet).
   Run `Scripts/build-release.ps1` on Windows: it builds the Windows installer and archive, the Linux package and every
   server package, and writes `SHA256SUMS-0.3.0.txt`. Add them to the same release and publish it.
2. In `updates.json` replace the version number in `version` and in the `url` of every system. The optional `notes`
   field is a "what is new" line the application shows.
3. Commit. The page updates itself, and applications of earlier versions say "A new version is available" at start.

`Scripts/App/build-installer.ps1` prints a ready entry for Windows.

A pre-release (a beta) is built the same way, but on GitHub tick **Set as a pre-release** and leave `updates.json`
alone: the update check offers it to nobody, and testers take it from the releases page. The version number is an
ordinary one, and the final release takes the next number.

## The old address of the repository

The repository used to be `DepthRel/Golether`; GitHub redirects that address (the code, the releases and the
downloads) to `Golether/Golether`. Never create a new repository `DepthRel/Golether`: GitHub would stop redirecting.

## A new picture

The page shows `assets/screenshot.jpg`, a lighter copy of `Docs/images/screenshot.png` (at most 1600 pixels wide).
After replacing the screenshot run `Scripts/Assets/update-site-screenshot.ps1` and commit both files. If the
proportions changed, correct the `width` and `height` of the picture in `index.html`: the script prints the new size.

## The rendezvous server address

`config.json` looks like `{ "rendezvous": "g1.…" }`. The string is made by the owner's utility
`Golether.AdminSettings` (a separate repository): enter `host:port` into it and save the result over `config.json`. An
empty string means "no server": the application creates a session without one and tells the host so.

The sealing is a mask against scanners, not a secret: the key is in the open code of the application
(`Golether.Security.Discovery.EndpointSeal`), and the server address is part of every invitation.

An optional `signal` field names the signalling of the same server for browsers, sealed the same way: the utility
takes its `host:port` in a second field (`signal.example.org:8443`) and writes
`{ "rendezvous": "g1.…", "signal": "g1.…" }`. The signalling is always `wss://<host:port>/signal`, behind a
certificate browsers trust; with Cloudflare in front of the server it needs no ports 80 and 443 (the administrator's
guide of the server, "Browsers (prototype)", "Behind Cloudflare"). With the field, the sessions the application hosts
in the "Automatically" mode take guests from a browser, and their invitations offer the `watch/` page of the browser
version; a field that does not open is ignored, and without it the applications work as before.

## The browser version

The same workflow builds `Sources/Apps/Golether.Browser` and publishes it at <https://golether.github.io/watch/>,
which is not a folder here. The published `watch/config.json` is a copy of this `config.json`: the page opens both
seals itself (WebCrypto), signals at the server of `signal` and uses the rendezvous and STUN at the server of
`rendezvous`, so no address stands on the site in the open. While `config.json` has no `signal` field the page is
published all the same: it opens, says on the start screen that sessions in the browser are not available yet and
joins only invitations that name a signalling of their own; sealing a `signal` into `config.json` makes sessions
available with the next publication. A repository variable `BROWSER_SERVER` of earlier versions is no longer read and
can be deleted.

The page opens with any server, but sessions start only when that server runs a version with the `Signal` section
enabled, behind a certificate browsers trust, with `https://golether.github.io` among its `AllowedOrigins` (the
administrator's guide of the server, "Browsers (prototype)"). `config.json` of `Golether.Browser` itself names servers
on the developer's computer in the open (`signal`, `stun`, `meet`) for a local run.

## Rules of the page

- Nothing is loaded from other servers but one thing: the main page counts visits with Umami Cloud (no cookies), its
  script from `cloud.umami.is` and its reports to `gateway.umami.is`. Besides visits it counts the `download` event of
  the download links (`data-umami-event-package`: `windows`, `windows-zip`, `linux-deb`, `linux-archive`, `macos`,
  `macos-intel`;
  `data-umami-event-place`: `card` or `hero`, the button at the top, which `site.js` marks for the system of the
  visitor), and the `try-online` event of the links to the browser version (`data-umami-event-place`: `header` or
  `hero`). It counts only on `golether.github.io`, keeps
  the part of the address after `#` and `?` out of its reports and skips browsers that ask not to be tracked. The
  invitation page has no counter: a script of another server would see the invitation in its address. Both pages
  pin what they may load with a `Content-Security-Policy` header; there are no inline scripts or styles either.
- The invitation stands after `#`, so the browser does not send it to the server. The invitation page passes it
  nowhere and removes it from the address bar at once.
- The English text is written right in the HTML (the page reads fine without scripts); the other languages (Deutsch,
  Español, Français, Português, Русский) are tables in `assets/site.js` and `assets/join.js`. Without scripts the
  language picker is hidden.
- The language is chosen like in the application: the one the visitor picked before (kept in `localStorage`),
  otherwise the first language of the browser the page speaks, otherwise English.
- A new language: one more entry in the `list` of `assets/languages.js`, its flag in `assets/flags/<code>.svg`
  (3:2), and one more table in `assets/site.js` and `assets/join.js`. A table must have every key the English page
  has.
- The "User guide" link in the footer leads to the documentation in the language of the page: the `docs` table in
  `assets/languages.js` maps a language to its folder in `Docs/` (`en` → `En`, `de` → `De`, …). A language without a
  translated guide gets the English one; a new documentation language is one more entry in that table.

## Look at it locally

Any static server in this folder, for example `npx http-server . -p 8765`, then <http://127.0.0.1:8765/>. Opening
`index.html` as a file does not work: the security policy of the page allows only its own site.
