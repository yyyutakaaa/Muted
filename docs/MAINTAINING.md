# Beheer

Dit zijn de dingen die niet in de code kunnen zitten: instellingen op GitHub, accounts
en de eerste keer indienen bij winget en SignPath. Alles is eenmalig. Daarna doen de
workflows in `.github/workflows` de rest.

## In een keer

Met de GitHub CLI (`winget install GitHub.cli`, dan `gh auth login`):

```powershell
.\scripts\setup-github.ps1
```

Het script vraagt bij elke stap of je door wilt en zet Discussions aan, zet Pages aan en
bewaart de tokens als geheim in de repo. Er worden geen tokens opgeslagen op je computer.

## Per onderdeel

### Discussions

Instellingen, Features, vink Discussions aan. Maak daarna de categorieën "Q&A" en
"Show your setup" aan; de formulieren in `.github/DISCUSSION_TEMPLATE` horen daarbij.

### Website (GitHub Pages)

Instellingen, Pages, bij Source kies je "GitHub Actions". De workflow `Pages` zet
`docs/index.html` online op `https://yyyutakaaa.github.io/Muted/`. Alleen die pagina en de
afbeeldingen worden gepubliceerd. Start hem de eerste keer met "Run workflow" bij Actions.

### winget

De eerste keer moet je zelf indienen, omdat winget een nieuw pakket alleen via een pull
request op `microsoft/winget-pkgs` accepteert:

```powershell
.\scripts\submit-winget.ps1
```

Het script controleert de bestanden, installeert `wingetcreate` als dat nodig is en vraagt
om een token (scope `public_repo`). Na goedkeuring door Microsoft werkt
`winget install yyyutakaaa.Muted`. Sla daarna hetzelfde soort token op als geheim
`WINGET_TOKEN` (dat doet `setup-github.ps1` ook), dan dient elke stabiele release zichzelf in.

### Scoop

Geen actie nodig. De repo is zelf een bucket en elke stabiele release werkt
`bucket/muted.json` bij. Gebruikers installeren met:

```powershell
scoop bucket add muted https://github.com/yyyutakaaa/Muted
scoop install muted/muted
```

### Code signing (SignPath)

SignPath ondertekent open-source projecten gratis, maar keurt ze eerst goed.

1. Vraag aan op <https://signpath.org/apply>. Ze willen onder meer dat de repo een
   open-source licentie heeft (MIT, staat er al) en dat releases uit GitHub Actions komen
   (ook al zo).
2. Zet na goedkeuring in SignPath een project aan met slug `muted` en een signing policy met
   slug `release-signing`. Plak daar de inhoud van
   `packaging/signpath/artifact-configuration.xml` in als artifact configuration.
   Gebruik je andere namen, zet dan de variabelen `SIGNPATH_PROJECT_SLUG` en
   `SIGNPATH_POLICY_SLUG` in de repo.
3. Bewaar `SIGNPATH_ORGANIZATION_ID` (variabele) en `SIGNPATH_API_TOKEN` (geheim), of draai
   `setup-github.ps1`.
4. SignPath eist dat de tekst hieronder in de README of op de website staat. Plaats hem pas
   op het moment dat de ondertekening echt aan staat, anders klopt hij niet:

   > **Code signing policy.** Free code signing provided by [SignPath.io](https://signpath.io),
   > certificate by [SignPath Foundation](https://signpath.org). Releases are built from this
   > repository by GitHub Actions. Muted does not send any data to anyone; see the README for
   > what it stores on your PC.

Tot die tijd slaat de workflow de ondertekening over en werkt alles als voorheen. Windows
SmartScreen waarschuwt dan nog wel bij een nieuwe download.

## Een release maken

Tag een versie (`git tag v0.7.0`, `git push origin v0.7.0`) of start "Release" bij Actions en
vul het versienummer in. De workflow bouwt, test, ondertekent (als dat is ingesteld),
publiceert de installer en werkt Scoop en winget bij. Je hoeft zelf geen versienummers in
bestanden aan te passen.
