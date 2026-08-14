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

PuerTS **is** redistributed here, in `Editor/Puerts/Vendor/`: the managed sources verbatim,
its JavaScript bootstrap, and the QuickJS and PuerTS core native libraries for desktop
editors (Windows, macOS universal, Linux, x64). It powers instant local rebuilds. Android,
iOS, WebGL and OpenHarmony binaries are not included, and neither is the IL2CPP wrapper
generator, because the engine is editor-only and cannot reach a player build.

The assembly is renamed to `Polyfork.Puerts` and is not auto-referenced; the C# namespace is
untouched. `Tools~/vendor-puerts.py` reproduces the vendored tree from upstream release
archives and documents what is taken and left.

Homepage: https://github.com/Tencent/puerts
Version: 3.0.2
Licence: **BSD 3-Clause**, Copyright (C) 2020 Tencent. All rights reserved.

The full licence, including the third-party components PuerTS itself carries, is reproduced
verbatim at `Editor/Puerts/Vendor/LICENSE-PuerTS.txt`, which is what clause 2 asks of a
binary redistribution:

> Redistribution and use in source and binary forms, with or without modification, are
> permitted provided that the following conditions are met:
>
> 1. Redistributions of source code must retain the above copyright notice, this list of
>    conditions and the following disclaimer.
> 2. Redistributions in binary form must reproduce the above copyright notice, this list of
>    conditions and the following disclaimer in the documentation and/or other materials
>    provided with the distribution.
> 3. Neither the name of the copyright holder nor the names of its contributors may be used
>    to endorse or promote products derived from this software without specific prior written
>    permission.
>
> THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS
> OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF
> MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE
> COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
> EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
> SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
> HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR
> TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
> SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
