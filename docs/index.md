---
layout: home

hero:
  name: HOPPER
  text: Every client in sync, before the game starts.
  tagline: Self-hosted mod sync for Minecraft. One list in the dashboard, one jar in mods/, and nobody has to be told to update.
  image:
    src: /logo.svg
    alt: HOPPER
  actions:
    - theme: brand
      text: Self-host HOPPER
      link: /self-host
    - theme: alt
      text: What is HOPPER?
      link: /intro
    - theme: alt
      text: GitHub
      link: https://github.com/PianoNic/HOPPER

features:
  - title: No restart
    details: On Forge and NeoForge, mods download before the loader scans for them, so they load in the same launch.
  - title: Every loader generation
    details: Forge 1.12.x through current, NeoForge, Fabric and Quilt, from one shared core and a thin adapter each.
  - title: Zero client config
    details: The generated jar already carries its server's URL and token. Drop it in mods/ and it works.
  - title: Leaves your own mods alone
    details: Downloads land in hoppermods/, never in mods/. A jar HOPPER did not download is never deleted.
---
