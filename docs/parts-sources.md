# Where the illustration parts come from

Study Stash composes most illustrations from a library of pre-made, properly licensed vector parts instead of
having an AI draw every shape (see DESIGN.md, "Illustrations"). This page records which sources were looked at,
their licences (checked per file where a source mixes licences), what they cover, and the verdict. The credits the
app shows (About) and the README's list are generated from the library itself by `engine/tools/PartsImport`.

The repo is public and MIT-licensed, so a source must allow commercial reuse and modification without asking us to
relicense the app: CC0 / public domain, MIT, Apache-2.0, ISC or BSD, or CC BY (attribution, which the credits give).
Non-commercial (NC), share-alike (SA) and GPL-family art is never imported.

## Chosen

| Source | Licence (as checked) | What it covers | How it's used |
|---|---|---|---|
| [Bioicons](https://bioicons.com) ([repo](https://github.com/duerrsimon/bioicons), site code MIT) | **per file**, written in each file's path (`static/icons/<licence>/<category>/<author>/`). Only `cc-0`, `cc-by-3.0`, `cc-by-4.0`, `mit` and `bsd` paths are read; every `cc-by-sa-*` path is skipped. | Anatomy and physiology, cells and organelles, lab glassware and apparatus, chemistry, microbiology, plants, animals, a little computer hardware | The importer fetches files from the repo at a pinned commit; the licence and author come from the path |
| ↳ [Servier Medical Art](https://smart.servier.com/) (most of Bioicons' anatomy, organelles and glassware) | CC BY 3.0 Unported as distributed by Bioicons (Servier now publishes the same art under CC BY 4.0). Credit: "Servier Medical Art (smart.servier.com), CC BY 3.0, adapted" | Heart (whole, cut open, valves), organs, organelles, burette and stand, flasks, beakers | Servier's own site offers only PNG/PPTX and forbids automated downloads, so the SVGs come from Bioicons' copies, never from Servier's site |
| ↳ [DBCLS Togo Picture Gallery](https://togotv.dbcls.jp/en/pics.html) | CC BY 4.0; required credit "© 2016 DBCLS TogoTV / CC-BY-4.0" | Bones of the hand and foot, joints, animals, plants, lab equipment | Same; the large anatomical files are simplified on import |
| ↳ Bioicons' CC0 contributors (OpenClipart, Mariana Ruiz Villarreal, Simon Dürr and others) | CC0 1.0 | Glassware, lab stands, a prokaryote, computer hardware | Same |
| [Wokwi elements](https://github.com/wokwi/wokwi-elements) (npm `@wokwi/elements`) | MIT, © 2020 Uri Shaked | Arduino Uno, Nano and Mega, ESP32, LEDs, resistor, buttons, potentiometers, servo, stepper, sensors, displays, keypad | The importer reads the package's SVG templates with every light off, expands their repeated pin patterns into shapes, drops the glow filters |
| Study Stash's own parts (`engine/tools/PartsImport/house/`) | MIT, as the rest of this repo | Drone, robot-arm and machine parts, a server rack and its equipment: things no free illustration set covers well | Drawn for this library in the house style (by Claude, for this project), or cut out of illustrations the app's own illustrator drew |

## Looked at and not used

| Source | Licence | Why not |
|---|---|---|
| Fritzing parts (breadboard graphics) | CC BY-SA 3.0 | Share-alike |
| Wikimedia Commons anatomy (e.g. the classic heart diagram by Wapcaplet) | mostly CC BY-SA, some PD, per file | The good heart and hand diagrams are share-alike; the public-domain ones (LadyofHats) are already in Bioicons under CC0 |
| game-icons.net | CC BY 3.0 | Usable, but one-colour silhouettes: they don't look like the textbook figures the rest of the library is |
| Tabler (MIT), Lucide (ISC), Phosphor (MIT), Material Symbols (Apache-2.0) | permissive | Line icons at 24 px: fine for UI, too plain to be a part of an illustration |
| PhyloPic | per image: CC0, CC BY, and some CC BY-NC | Silhouettes; NC images would need filtering one by one, for little gain |
| Reactome icon library | CC BY 4.0 | Molecular icons; Bioicons already covers the cell biology at a better illustration scale |
| Red Hat network-automation/networking-icons | Apache-2.0 | Flat topology symbols (router, switch, firewall); kept in mind for network diagrams, which flowcharts already draw |
| bwks/network-icons-svg | GPL-3.0 | GPL |
| Isoflow isopacks | "varies" per pack, many vendor (AWS, Azure, GCP) icons under their own terms | Unclear per-icon terms |
| Openclipart / FreeSVG drones | CC0 | Usable, but clip-art drones with no parts to point at; the house drone parts are better |
| Iconscout, Flaticon, Icons8, Freepik, Reshot | proprietary licences | Not open |
| DBCLS server rack (Bioicons) | CC BY 4.0 | 1 MB, 7,000 paths: too heavy for a part; the house rack is drawn instead |

## How a part gets in (engine/tools/PartsImport)

1. Fetched from its source at a pinned version (Bioicons commit, Wokwi package version) into a local cache.
2. Made plain: XML declarations, document types, editor metadata and `<switch>` wrappers removed; CSS classes from
   `<style>` written onto each element; Wokwi's templates evaluated with every LED off; pattern fills tiled as shapes.
3. Through `SafeSvg.Clean`, the same check every drawing in the notes gets: a part it refuses is not imported (a test
   holds that a hostile part is refused). What survives is only shapes, text, gradients and clips.
4. Recoloured onto the house palette (the illustrator's palette by material: each colour moved to the nearest point
   on the nearest material's light-to-outline ramp, in OKLab), so parts from different sources look like one set
   and follow the dark theme the way drawn illustrations do.
5. Cropped to what it draws, its numbers rounded to what shows, heavy files simplified, ids made its own.
6. Named: an id, a display name, tags and synonyms, a category, a view, its real size, and where useful its named
   regions (the left ventricle of a heart, the USB port of a board: these become parts a student can point at) and
   ports (points other parts and connectors attach to).
7. Written, with its source, author and licence, into `src/StudyStash.Core/Rich/PartsLibrary.xml`, and the credits
   into `PartsCredits.md` (README) and the app's About.
