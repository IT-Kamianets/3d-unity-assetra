# 3d-unity-assetra

Unity package (`com.webart.assetra`) that connects any of our games to **Assetra**
(assetra.itkamianets.com): one library of 3D models shared between games. Models made in a game are
published to Assetra with as much metadata as Unity can extract, and the next game pulls them back.

## Install

Package Manager → *Add package from git URL*:

```
https://github.com/IT-Kamianets/3d-unity-assetra.git
```

## What it does

- **Window → Assetra**: lists your assets, publishes the selected model/prefab.
- **ModelMetadataExtractor**: reads polygons, vertices, meshes, LODs, bones, animation clips,
  blend shapes, materials, texture maps/resolution/PBR workflow, real-world dimensions, file size,
  format, render pipeline and the Unity version, and tags the model with the game it came from
  (`game:<product name>`).
- **AssetraClient**: `/api/assetra/asset` (list, get, put, delete) and file upload through
  `/api/cloudflare`.

## API

Backend module: `server/assetra` of it.webart.work. Every call sends the Google ID token of the
Assetra sign-in as `Authorization: Bearer <token>`. Assets are private to their owner (admins see all).

## Status (v0.1.0)

Scaffold, not yet opened in Unity. Open points:

- Google sign-in from the editor (for now paste the ID token into the window).
- Files above 7 MB (needs the `/api/cloudflare/upload-intent` direct upload).
- Importing a listed model into the project (GLB through glTFast, or a Unity-native package).
- Preview image (`images`) and low-poly (`modelLow`).
- Reading the whole team's library rather than only your own assets (needs a backend change).

Unity writes `.meta` files on first import: commit them, packages need stable GUIDs.
