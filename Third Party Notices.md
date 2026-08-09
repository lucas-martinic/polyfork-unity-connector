# Third Party Notices

This package redistributes the components below. Each remains under its own
licence, reproduced here in full as those licences require.

Both are delivered through the optional **Local Baking** sample rather than the
core package, so a project that does not import that sample ships neither.

---

## three.js

A trimmed build of three.js is bundled as
`Samples~/LocalBaking/Resources/Polyfork/three-trimmed.txt`. Polyfork's asset
modules are authored against the three.js geometry API, so evaluating one
locally requires the library those modules import. The build is reduced to the
classes the asset modules actually reference; it is otherwise unmodified.

Homepage: https://threejs.org
Source: https://github.com/mrdoob/three.js

```
The MIT License

Copyright © 2010-2026 three.js authors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## PuerTS

PuerTS is **not** redistributed here. The Local Baking sample compiles against
it only when you install it yourself, and the package compiles and runs without
it. It is listed for completeness because the sample names it as a prerequisite.

Homepage: https://github.com/Tencent/puerts
Licence: MIT, Copyright (c) 2020 Tencent
