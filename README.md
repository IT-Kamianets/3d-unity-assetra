# 3d-unity-assetra

Unity package (`com.itkamianets.3d-unity-assetra`) that connects any of our games to **Assetra**
(assetra.itkamianets.com): one library of 3D models shared between games. A game lists the models it
needs in `assetra.json`, Assetra fills them, and the filled models come back into the game.

## Install

Package Manager → *Add package from git URL*:

```
https://github.com/IT-Kamianets/3d-unity-assetra.git
```

## What it does

- **Window → Assetra → Project sync**: sends the game's `assetra.json` to its Assetra project and
  downloads the models that were filled there (see below).
- **Window → Assetra → Collections**: lists the library's collections; an admin can create one.
- **Window → Assetra → Publish a model**: lists your assets, publishes the selected model/prefab.
- **ModelMetadataExtractor**: reads polygons, vertices, meshes, LODs, bones, animation clips,
  blend shapes, materials, texture maps/resolution/PBR workflow, real-world dimensions, file size,
  format, render pipeline and the Unity version, and tags the model with the game it came from
  (`game:<product name>`).
- **AssetraClient** (`/api/assetra`): assets, projects, placeholders, models and collections, plus
  file upload through `/api/cloudflare`.

## `assetra.json` and the sync

`assetra.json` in the game's repository root is a `3d-asset-schema` library. The game owns the list:
one asset of `kind: "model"` per model, with `id`, `bounds`, a `placeholder` shape and `metadata`.
The project is named in `metadata.properties.assetra`:

```json
{ "project": "math-runner", "name": "Math Runner", "modelsDir": "Assets/MathRunner/Art/Models" }
```

A sync (`AssetraSync.RunAsync`, or the *Sync project* button):

1. sends the list to `POST /api/assetra/project/<project>/sync`; the project and its placeholders are
   created or updated, and a placeholder that is already filled keeps its model;
2. downloads each placeholder that was filled in Assetra into its `source.uri` (or
   `modelsDir/<id>.glb`, with `/` written as `_`, when the asset has no `source` yet) and sets that
   `source` in `assetra.json`.

It never removes an entry, never overwrites a file that exists, and never writes outside the
repository. *Prune* (off by default) deletes placeholders from the Assetra project that are not in the
file; it never deletes models.

## Sign-in

Every call sends a **Firebase ID token of the Assetra project (`vroom-8ef42`)** as
`Authorization: Bearer <token>`. The API refuses tokens of any other Firebase project.

*Sign in with Google* (Window → Assetra) opens the browser, Google redirects to a one-shot listener on
`127.0.0.1`, the code is exchanged with PKCE, and Firebase turns the Google token into the Assetra
one. It is refreshed from a saved refresh token. It needs an **OAuth client of type Desktop** in the
Google Cloud project of `vroom-8ef42`: paste its client id and secret into *Sign-in settings*, or load the
JSON Google gives for it. They are kept in the developer's Editor preferences, never in the project.

Whether an account is an admin is decided by the API from its verified Google email.
Without a Desktop client you can still paste a token from the Assetra site into *Sign-in settings*; it
works for about an hour.

## API

Backend module: `server/assetra` of it.webart.work. Assets are private to their owner (admins see
all); projects follow the same rule; models and collections are public to read and admin to write.

| Call | Route |
|---|---|
| `ListProjectsAsync / GetProjectAsync / PutProjectAsync / DeleteProjectAsync` | `/project`, `/project/:slug` |
| `SyncProjectAsync(slug, body)` | `POST /project/:slug/sync` |
| `ListPlaceholdersAsync / GetPlaceholderAsync / PutPlaceholderAsync / FillPlaceholderAsync / DeletePlaceholderAsync` | `/project/:slug/placeholder[/:id]`, with `/` in the id written as `__` |
| `ListModelsAsync / GetModelAsync` | `/model` |
| `ListCollectionsAsync / GetCollectionAsync / CreateCollectionAsync` | `/collection` |

## Status (v0.2.0)

Compiles in Unity 6000.6.8f1; 18 edit-mode tests cover the sign-in requests, PKCE, `assetra.json`
reading, the sync body and the path check. Not yet tried against the live API with a real Google
sign-in. Open points:

- Files above 7 MB (needs the `/api/cloudflare/upload-intent` direct upload).
- Importing a listed model into the project (GLB through glTFast, or a Unity-native package).
- Preview image (`images`) and low-poly (`modelLow`).
- Creating or editing models of the library from Unity (admin routes are not wrapped yet).

Unity writes `.meta` files on first import: commit them, packages need stable GUIDs.
