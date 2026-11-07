#include <algorithm>
#include <cctype>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <set>
#include <sstream>
#include <string>
#include <unordered_map>
#include <vector>

#include "ExtraData.hpp"
#include "NifFile.hpp"
#include "Shaders.hpp"
#include "bhk.hpp"
#include "nlohmann/json.hpp"

using njson = nlohmann::json;
namespace fs = std::filesystem;

namespace {

struct ObjMesh {
    std::vector<nifly::Vector3> verts;
    std::vector<nifly::Vector2> uvs;
    std::vector<nifly::Vector3> norms;
    std::vector<nifly::Triangle> tris;
    bool hasUV = false;
    bool hasNorm = false;
};

struct Corner { int v = 0, vt = 0, vn = 0; };

int resolveIdx(int idx, size_t count) {
    if (idx > 0) return idx - 1;
    if (idx < 0) return static_cast<int>(count) + idx;
    return -1;
}

bool loadObj(const fs::path& path, bool fromBlender, ObjMesh& out, std::string& err) {
    std::ifstream in(path);
    if (!in) { err = "cannot open OBJ: " + path.string(); return false; }

    std::vector<nifly::Vector3> P;
    std::vector<nifly::Vector2> T;
    std::vector<nifly::Vector3> N;
    std::unordered_map<std::string, uint16_t> uniq;

    auto emit = [&](const Corner& c) -> int {
        std::string key = std::to_string(c.v) + "/" + std::to_string(c.vt) + "/" +
                          std::to_string(c.vn);
        auto it = uniq.find(key);
        if (it != uniq.end()) return it->second;
        if (out.verts.size() >= 0xFFFF) return -1;

        int pi = resolveIdx(c.v, P.size());
        if (pi < 0 || pi >= static_cast<int>(P.size())) return -1;
        nifly::Vector3 pos = P[pi];
        if (fromBlender) pos = nifly::Vector3(pos.x, -pos.z, pos.y);
        out.verts.push_back(pos);

        nifly::Vector2 uv(0.f, 0.f);
        if (c.vt != 0) {
            int ti = resolveIdx(c.vt, T.size());
            if (ti >= 0 && ti < static_cast<int>(T.size())) { uv = T[ti]; out.hasUV = true; }
        }
        out.uvs.push_back(uv);

        if (c.vn != 0) {
            int ni = resolveIdx(c.vn, N.size());
            if (ni >= 0 && ni < static_cast<int>(N.size())) {
                nifly::Vector3 nrm = N[ni];
                if (fromBlender) nrm = nifly::Vector3(nrm.x, -nrm.z, nrm.y);
                out.norms.push_back(nrm);
                out.hasNorm = true;
            } else {
                out.norms.push_back(nifly::Vector3(0, 0, 1));
            }
        } else {
            out.norms.push_back(nifly::Vector3(0, 0, 1));
        }

        uint16_t idx = static_cast<uint16_t>(out.verts.size() - 1);
        uniq[key] = idx;
        return idx;
    };

    auto parseCorner = [](const std::string& tok) -> Corner {
        Corner c;
        std::string parts[3];
        int p = 0;
        for (char ch : tok) {
            if (ch == '/') { if (++p > 2) break; }
            else parts[p].push_back(ch);
        }
        if (!parts[0].empty()) c.v = std::stoi(parts[0]);
        if (!parts[1].empty()) c.vt = std::stoi(parts[1]);
        if (!parts[2].empty()) c.vn = std::stoi(parts[2]);
        return c;
    };

    std::string line;
    while (std::getline(in, line)) {
        std::istringstream ls(line);
        std::string tag;
        ls >> tag;
        if (tag == "v") {
            nifly::Vector3 p; ls >> p.x >> p.y >> p.z; P.push_back(p);
        } else if (tag == "vt") {
            nifly::Vector2 t; ls >> t.u >> t.v; t.v = 1.f - t.v;
            T.push_back(t);
        } else if (tag == "vn") {
            nifly::Vector3 n; ls >> n.x >> n.y >> n.z; N.push_back(n);
        } else if (tag == "f") {
            std::vector<int> face;
            std::string tok;
            while (ls >> tok) {
                int e = emit(parseCorner(tok));
                if (e < 0) { err = "OBJ exceeds 65535 unique verts or has a bad index"; return false; }
                face.push_back(e);
            }
            for (size_t i = 2; i < face.size(); ++i)
                out.tris.emplace_back(static_cast<uint16_t>(face[0]),
                                      static_cast<uint16_t>(face[i - 1]),
                                      static_cast<uint16_t>(face[i]));
        }
    }
    if (out.verts.empty() || out.tris.empty()) { err = "OBJ has no geometry"; return false; }
    return true;
}

std::string argVal(int argc, char** argv, const std::string& flag) {
    for (int i = 0; i < argc - 1; ++i)
        if (flag == argv[i]) return argv[i + 1];
    return "";
}
bool argHas(int argc, char** argv, const std::string& flag) {
    for (int i = 0; i < argc; ++i)
        if (flag == argv[i]) return true;
    return false;
}

void addBoxCollision(nifly::NifFile& nif, nifly::NiNode* root,
                     const nifly::Vector3& centerGame, const nifly::Vector3& halfExtentGame) {
    using namespace nifly;
    const float HK = 0.0142875f;

    auto box = std::make_unique<bhkBoxShape>();
    box->dimensions = Vector3(halfExtentGame.x * HK, halfExtentGame.y * HK, halfExtentGame.z * HK);
    box->radius = 0.0139f;
    box->SetMaterial(0);
    uint32_t boxId = nif.GetHeader().AddBlock(std::move(box));

    auto body = std::make_unique<bhkRigidBodyT>();
    body->shapeRef.index = boxId;
    body->collisionFilter.layer = 1;
    body->collisionFilterCopy = body->collisionFilter;
    Vector4 t; t.x = centerGame.x * HK; t.y = centerGame.y * HK; t.z = centerGame.z * HK; t.w = 0.f;
    body->translation = t;
    QuaternionXYZW q; q.x = 0.f; q.y = 0.f; q.z = 0.f; q.w = 1.f;
    body->rotation = q;
    body->mass = 0.f;
    uint32_t bodyId = nif.GetHeader().AddBlock(std::move(body));

    auto collObj = std::make_unique<bhkCollisionObject>();
    collObj->flags = 1;
    collObj->bodyRef.index = bodyId;
    collObj->targetRef.index = nif.GetBlockID(root);
    uint32_t collId = nif.GetHeader().AddBlock(std::move(collObj));

    root->collisionRef.index = collId;
}

struct ObjGroup {
    std::string name;
    ObjMesh mesh;
};

bool loadObjGroups(const fs::path& path, bool fromBlender, std::vector<ObjGroup>& out,
                   std::string& err) {
    std::ifstream in(path);
    if (!in) { err = "cannot open OBJ: " + path.string(); return false; }

    std::vector<nifly::Vector3> P;
    std::vector<nifly::Vector2> T;
    std::vector<nifly::Vector3> N;

    ObjGroup* cur = nullptr;
    std::unordered_map<std::string, uint16_t> uniq;

    auto startGroup = [&](const std::string& name) {
        out.push_back(ObjGroup{ name, ObjMesh{} });
        cur = &out.back();
        uniq.clear();
    };

    auto emit = [&](const Corner& c) -> int {
        if (!cur) startGroup("Shape");
        ObjMesh& m = cur->mesh;
        std::string key = std::to_string(c.v) + "/" + std::to_string(c.vt) + "/" +
                          std::to_string(c.vn);
        auto it = uniq.find(key);
        if (it != uniq.end()) return it->second;
        if (m.verts.size() >= 0xFFFF) return -1;

        int pi = resolveIdx(c.v, P.size());
        if (pi < 0 || pi >= static_cast<int>(P.size())) return -1;
        nifly::Vector3 pos = P[pi];
        if (fromBlender) pos = nifly::Vector3(pos.x, -pos.z, pos.y);
        m.verts.push_back(pos);

        nifly::Vector2 uv(0.f, 0.f);
        if (c.vt != 0) {
            int ti = resolveIdx(c.vt, T.size());
            if (ti >= 0 && ti < static_cast<int>(T.size())) { uv = T[ti]; m.hasUV = true; }
        }
        m.uvs.push_back(uv);

        if (c.vn != 0) {
            int ni = resolveIdx(c.vn, N.size());
            if (ni >= 0 && ni < static_cast<int>(N.size())) {
                nifly::Vector3 nrm = N[ni];
                if (fromBlender) nrm = nifly::Vector3(nrm.x, -nrm.z, nrm.y);
                m.norms.push_back(nrm);
                m.hasNorm = true;
            } else {
                m.norms.push_back(nifly::Vector3(0, 0, 1));
            }
        } else {
            m.norms.push_back(nifly::Vector3(0, 0, 1));
        }

        uint16_t idx = static_cast<uint16_t>(m.verts.size() - 1);
        uniq[key] = idx;
        return idx;
    };

    auto parseCorner = [](const std::string& tok) -> Corner {
        Corner c;
        std::string parts[3];
        int p = 0;
        for (char ch : tok) {
            if (ch == '/') { if (++p > 2) break; }
            else parts[p].push_back(ch);
        }
        if (!parts[0].empty()) c.v = std::stoi(parts[0]);
        if (!parts[1].empty()) c.vt = std::stoi(parts[1]);
        if (!parts[2].empty()) c.vn = std::stoi(parts[2]);
        return c;
    };

    std::string line;
    while (std::getline(in, line)) {
        std::istringstream ls(line);
        std::string tag;
        ls >> tag;
        if (tag == "v") {
            nifly::Vector3 p; ls >> p.x >> p.y >> p.z; P.push_back(p);
        } else if (tag == "vt") {
            nifly::Vector2 t; ls >> t.u >> t.v; t.v = 1.f - t.v;
            T.push_back(t);
        } else if (tag == "vn") {
            nifly::Vector3 n; ls >> n.x >> n.y >> n.z; N.push_back(n);
        } else if (tag == "g" || tag == "o") {
            std::string name; ls >> name;
            if (name.empty()) name = "Shape" + std::to_string(out.size());
            startGroup(name);
        } else if (tag == "f") {
            std::vector<int> face;
            std::string tok;
            while (ls >> tok) {
                int e = emit(parseCorner(tok));
                if (e < 0) { err = "group exceeds 65535 unique verts or has a bad index"; return false; }
                face.push_back(e);
            }
            for (size_t i = 2; i < face.size(); ++i)
                cur->mesh.tris.emplace_back(static_cast<uint16_t>(face[0]),
                                            static_cast<uint16_t>(face[i - 1]),
                                            static_cast<uint16_t>(face[i]));
        }
    }

    out.erase(std::remove_if(out.begin(), out.end(),
                             [](const ObjGroup& g) {
                                 return g.mesh.verts.empty() || g.mesh.tris.empty();
                             }),
              out.end());
    if (out.empty()) { err = "OBJ has no geometry"; return false; }
    return true;
}

int cmdImportMulti(int argc, char** argv) {
    if (argc < 4) {
        std::cerr << "usage: niftool importmulti <in.obj> <out.nif> --spec <spec.json> [--from-blender]\n"
                  << "  spec.json: { \"GroupName\": { \"diffuse\": \"textures\\\\...\", \"normal\": \"...\" } }\n";
        return 2;
    }
    fs::path inObj = argv[2], outNif = argv[3];
    std::string specPath = argVal(argc, argv, "--spec");
    bool fromBlender = argHas(argc, argv, "--from-blender");

    njson spec;
    if (!specPath.empty()) {
        std::ifstream sf(specPath);
        if (!sf) { std::cerr << "RESULT: importmulti FAILED - cannot open spec\n"; return 1; }
        sf >> spec;
    }

    std::vector<ObjGroup> groups;
    std::string err;
    if (!loadObjGroups(inObj, fromBlender, groups, err)) {
        std::cerr << "RESULT: importmulti FAILED - " << err << "\n";
        return 1;
    }

    using namespace nifly;
    NifFile nif;
    nif.Create(NiVersion::getFO4());
    NiNode* root = nif.GetRootNode();
    if (!root) { std::cerr << "RESULT: importmulti FAILED - no root node\n"; return 1; }
    root->name.get() = outNif.stem().string();

    for (auto& g : groups) {
        ObjMesh& m = g.mesh;

        std::string shapeName = g.name;
        if (spec.contains(g.name) && spec.at(g.name).contains("name"))
            shapeName = spec.at(g.name)["name"].get<std::string>();

        NiShape* shape = nif.CreateShapeFromData(shapeName, &m.verts, &m.tris,
                                                 m.hasUV ? &m.uvs : nullptr,
                                                 m.hasNorm ? &m.norms : nullptr);
        if (!shape) { std::cerr << "RESULT: importmulti FAILED - CreateShapeFromData " << g.name << "\n"; return 1; }
        if (!m.hasNorm) nif.CalcNormalsForShape(shape);
        if (m.hasUV) nif.CalcTangentsForShape(shape);

        if (spec.contains(g.name)) {
            const auto& s = spec.at(g.name);
            if (s.contains("diffuse")) { std::string t = s["diffuse"].get<std::string>(); nif.SetTextureSlot(shape, t, 0); }
            if (s.contains("normal"))  { std::string t = s["normal"].get<std::string>();  nif.SetTextureSlot(shape, t, 1); }
            if (s.contains("material")) {
                if (auto* lsp = dynamic_cast<BSLightingShaderProperty*>(nif.GetShader(shape)))
                    lsp->rootMaterialName = NiStringRef(s["material"].get<std::string>());
            }
        }
        shape->UpdateBounds();
        std::cout << "  shape '" << shapeName << "'"
                  << (shapeName != g.name ? (" (from '" + g.name + "')") : std::string())
                  << ": " << m.verts.size() << " verts, "
                  << m.tris.size() << " tris\n";
    }

    auto bsx = std::make_unique<BSXFlags>();
    bsx->name = NiStringRef("BSX");
    bsx->integerData = 0u;
    uint32_t bsxId = nif.GetHeader().AddBlock(std::move(bsx));
    root->extraDataRefs.AddBlockRef(bsxId);

    NifSaveOptions opt;
    if (nif.Save(outNif, opt) != 0) { std::cerr << "RESULT: importmulti FAILED - save error\n"; return 1; }
    std::cout << "RESULT: importmulti OK -> " << outNif.string() << " (" << groups.size() << " shapes)\n";
    return 0;
}

int cmdImport(int argc, char** argv) {
    if (argc < 4) { std::cerr << "usage: niftool import <in.obj> <out.nif> [opts]\n"; return 2; }
    fs::path inObj = argv[2], outNif = argv[3];
    std::string material = argVal(argc, argv, "--material");
    std::string texD = argVal(argc, argv, "--tex-diffuse");
    std::string texN = argVal(argc, argv, "--tex-normal");
    bool fromBlender = argHas(argc, argv, "--from-blender");
    bool wantCollision = argVal(argc, argv, "--collision") == "box";

    ObjMesh m;
    std::string err;
    if (!loadObj(inObj, fromBlender, m, err)) { std::cerr << "RESULT: import FAILED - " << err << "\n"; return 1; }

    using namespace nifly;
    NifFile nif;
    nif.Create(NiVersion::getFO4());
    NiNode* root = nif.GetRootNode();
    if (!root) { std::cerr << "RESULT: import FAILED - no root node\n"; return 1; }
    root->name.get() = outNif.stem().string();

    std::string shapeName = outNif.stem().string();
    NiShape* shape = nif.CreateShapeFromData(shapeName, &m.verts, &m.tris,
                                             m.hasUV ? &m.uvs : nullptr,
                                             m.hasNorm ? &m.norms : nullptr);
    if (!shape) { std::cerr << "RESULT: import FAILED - CreateShapeFromData\n"; return 1; }

    if (!m.hasNorm) nif.CalcNormalsForShape(shape);
    if (m.hasUV) nif.CalcTangentsForShape(shape);

    if (auto* lsp = dynamic_cast<BSLightingShaderProperty*>(nif.GetShader(shape))) {
        if (!material.empty()) lsp->rootMaterialName = NiStringRef(material);
    }
    if (!texD.empty()) { std::string t = texD; nif.SetTextureSlot(shape, t, 0); }
    if (!texN.empty()) { std::string t = texN; nif.SetTextureSlot(shape, t, 1); }

    if (wantCollision) {
        Vector3 mn = m.verts[0], mx = m.verts[0];
        for (const auto& v : m.verts) {
            mn.x = std::min(mn.x, v.x); mn.y = std::min(mn.y, v.y); mn.z = std::min(mn.z, v.z);
            mx.x = std::max(mx.x, v.x); mx.y = std::max(mx.y, v.y); mx.z = std::max(mx.z, v.z);
        }
        Vector3 center((mn.x + mx.x) * 0.5f, (mn.y + mx.y) * 0.5f, (mn.z + mx.z) * 0.5f);
        Vector3 half((mx.x - mn.x) * 0.5f, (mx.y - mn.y) * 0.5f, (mx.z - mn.z) * 0.5f);
        addBoxCollision(nif, root, center, half);
    }

    auto bsx = std::make_unique<BSXFlags>();
    bsx->name = NiStringRef("BSX");
    bsx->integerData = wantCollision ? 2u : 0u;
    uint32_t bsxId = nif.GetHeader().AddBlock(std::move(bsx));
    root->extraDataRefs.AddBlockRef(bsxId);

    shape->UpdateBounds();

    NifSaveOptions opt;
    if (nif.Save(outNif, opt) != 0) { std::cerr << "RESULT: import FAILED - save error\n"; return 1; }

    std::cout << "RESULT: import OK -> " << outNif.string() << " (" << m.verts.size()
              << " verts, " << m.tris.size() << " tris"
              << (wantCollision ? ", +box collision" : "") << ")\n";
    return 0;
}

int cmdAddEffect(int argc, char** argv) {
    if (argc < 4) {
        std::cerr << "usage: niftool addeffect <in.nif> <out.nif> --from <shape> --material <m.bgem>\n"
                     "                          [--name <newshape>] [--offset <units>] [--tex-source <d.dds>]\n";
        return 2;
    }
    using namespace nifly;
    std::filesystem::path inNif = argv[2], outNif = argv[3];
    std::string fromShape, material, newName, texSource;
    float offset = 0.05f;

    for (int i = 4; i < argc; ++i) {
        std::string a = argv[i];
        auto next = [&]() -> std::string { return (i + 1 < argc) ? argv[++i] : std::string(); };
        if (a == "--from") fromShape = next();
        else if (a == "--material") material = next();
        else if (a == "--name") newName = next();
        else if (a == "--tex-source") texSource = next();
        else if (a == "--offset") { try { offset = std::stof(next()); } catch (...) {} }
    }
    if (fromShape.empty() || material.empty()) {
        std::cerr << "RESULT: addeffect FAILED - --from and --material are required\n";
        return 2;
    }
    if (newName.empty()) newName = fromShape + "Effect";

    NifFile nif;
    if (nif.Load(inNif) != 0) { std::cerr << "RESULT: addeffect FAILED - load error\n"; return 1; }

    NiShape* src = nullptr;
    for (auto* s2 : nif.GetShapes())
        if (s2->name.get() == fromShape) { src = s2; break; }
    if (!src) {
        std::cerr << "RESULT: addeffect FAILED - no shape named '" << fromShape << "'\n";
        return 1;
    }

    std::vector<Vector3> verts, norms;
    std::vector<Triangle> tris;
    std::vector<Vector2> uvs;
    if (!nif.GetVertsForShape(src, verts) || verts.empty()) {
        std::cerr << "RESULT: addeffect FAILED - source shape has no verts\n"; return 1;
    }
    src->GetTriangles(tris);
    const bool hasUV   = nif.GetUvsForShape(src, uvs) && uvs.size() == verts.size();
    const auto* srcN   = nif.GetNormalsForShape(src);
    const bool hasNorm = srcN && srcN->size() == verts.size();
    if (hasNorm) norms = *srcN;

    if (offset != 0.0f && hasNorm)
        for (size_t i = 0; i < verts.size(); ++i) {
            verts[i].x += norms[i].x * offset;
            verts[i].y += norms[i].y * offset;
            verts[i].z += norms[i].z * offset;
        }

    NiShape* dst = nif.CreateShapeFromData(newName, &verts, &tris,
                                           hasUV ? &uvs : nullptr,
                                           hasNorm ? &norms : nullptr);
    if (!dst) { std::cerr << "RESULT: addeffect FAILED - CreateShapeFromData\n"; return 1; }
    if (hasUV) nif.CalcTangentsForShape(dst);

    nif.DeleteShader(dst);

    auto eff = std::make_unique<BSEffectShaderProperty>();
    eff->name = NiStringRef(material);
    if (!texSource.empty()) eff->sourceTexture.get() = texSource;
    eff->baseColorScale = 1.0f;
    eff->falloffStartOpacity = 1.0f;
    eff->falloffStopOpacity = 1.0f;
    eff->softFalloffDepth = 0.0f;
    uint32_t effId = nif.GetHeader().AddBlock(std::move(eff));

    auto* ref = dst->ShaderPropertyRef();
    if (!ref) { std::cerr << "RESULT: addeffect FAILED - shape takes no shader ref\n"; return 1; }
    ref->index = effId;

    dst->UpdateBounds();

    NifSaveOptions opt;
    if (nif.Save(outNif, opt) != 0) { std::cerr << "RESULT: addeffect FAILED - save error\n"; return 1; }

    std::cout << "RESULT: addeffect OK -> " << outNif.string() << " ('" << newName
              << "' cloned from '" << fromShape << "', " << verts.size() << " verts, "
              << tris.size() << " tris, material=" << material
              << ", offset=" << offset << ")\n";
    return 0;
}

int cmdAddOverlay(int argc, char** argv) {
    if (argc < 4) {
        std::cerr << "usage: niftool addoverlay <in.nif> <out.nif> --from <shape>\n"
                     "                           [--name <newshape>] [--offset <units>]\n"
                     "                           [--tex <d.dds>] [--alpha-flags <n>]\n";
        return 2;
    }
    using namespace nifly;
    std::filesystem::path inNif = argv[2], outNif = argv[3];
    std::string fromShape, newName, tex;
    float offset = 0.05f;
    float dir[3] = { 0.0f, 0.0f, 1.0f };
    bool useDir = false;
    uint16_t alphaFlags = 237;

    for (int i = 4; i < argc; ++i) {
        std::string a = argv[i];
        auto next = [&]() -> std::string { return (i + 1 < argc) ? argv[++i] : std::string(); };
        if (a == "--from") fromShape = next();
        else if (a == "--name") newName = next();
        else if (a == "--tex") tex = next();
        else if (a == "--offset") { try { offset = std::stof(next()); } catch (...) {} }
        else if (a == "--alpha-flags") { try { alphaFlags = (uint16_t)std::stoul(next()); } catch (...) {} }
        else if (a == "--dir") {
            std::stringstream ds(next()); std::string t; int k = 0;
            while (std::getline(ds, t, ',') && k < 3) { try { dir[k++] = std::stof(t); } catch (...) {} }
            useDir = true;
        }
    }
    if (fromShape.empty()) { std::cerr << "RESULT: addoverlay FAILED - --from is required\n"; return 2; }
    if (newName.empty()) newName = fromShape + "Overlay";

    NifFile nif;
    if (nif.Load(inNif) != 0) { std::cerr << "RESULT: addoverlay FAILED - load error\n"; return 1; }

    NiShape* src = nullptr;
    for (auto* s2 : nif.GetShapes()) if (s2->name.get() == fromShape) { src = s2; break; }
    if (!src) { std::cerr << "RESULT: addoverlay FAILED - no shape named '" << fromShape << "'\n"; return 1; }

    std::vector<Vector3> verts, norms;
    std::vector<Triangle> tris;
    std::vector<Vector2> uvs;
    if (!nif.GetVertsForShape(src, verts) || verts.empty()) {
        std::cerr << "RESULT: addoverlay FAILED - source shape has no verts\n"; return 1;
    }
    src->GetTriangles(tris);
    const bool hasUV = nif.GetUvsForShape(src, uvs) && uvs.size() == verts.size();
    const auto* srcN = nif.GetNormalsForShape(src);
    const bool hasNorm = srcN && srcN->size() == verts.size();
    if (hasNorm) norms = *srcN;
    if (!hasUV) { std::cerr << "RESULT: addoverlay FAILED - source has no UVs; overlay would be unmappable\n"; return 1; }

    if (offset != 0.0f) {
        if (useDir || !hasNorm) {
            for (auto& v : verts) { v.x += dir[0] * offset; v.y += dir[1] * offset; v.z += dir[2] * offset; }
        } else {
            for (size_t i = 0; i < verts.size(); ++i) {
                verts[i].x += norms[i].x * offset;
                verts[i].y += norms[i].y * offset;
                verts[i].z += norms[i].z * offset;
            }
        }
    }

    NiShape* dst = nif.CreateShapeFromData(newName, &verts, &tris, &uvs, hasNorm ? &norms : nullptr);
    if (!dst) { std::cerr << "RESULT: addoverlay FAILED - CreateShapeFromData\n"; return 1; }
    nif.CalcTangentsForShape(dst);

    std::string slot0 = tex;
    if (slot0.empty()) nif.GetTextureSlot(src, slot0, 0);
    if (slot0.empty()) { std::cerr << "RESULT: addoverlay FAILED - no diffuse to inherit; pass --tex\n"; return 1; }
    nif.SetTextureSlot(dst, slot0, 0);

    auto alpha = std::make_unique<NiAlphaProperty>();
    alpha->flags = alphaFlags;
    uint32_t alphaId = nif.GetHeader().AddBlock(std::move(alpha));
    auto* aref = dst->AlphaPropertyRef();
    if (!aref) { std::cerr << "RESULT: addoverlay FAILED - shape takes no alpha ref\n"; return 1; }
    aref->index = alphaId;

    dst->UpdateBounds();

    NifSaveOptions opt;
    if (nif.Save(outNif, opt) != 0) { std::cerr << "RESULT: addoverlay FAILED - save error\n"; return 1; }

    std::cout << "RESULT: addoverlay OK -> " << outNif.string() << " ('" << newName
              << "' from '" << fromShape << "', " << verts.size() << " verts, offset " << offset
              << ", diffuse " << slot0 << ", alphaFlags " << alphaFlags
              << " [blend=" << ((alphaFlags & 1) ? "on" : "OFF")
              << " test=" << (((alphaFlags >> 9) & 1) ? "on" : "off") << "])\n";
    return 0;
}

int cmdFix(int argc, char** argv) {
    if (argc < 4) { std::cerr << "usage: niftool fix <in.nif> <out.nif>\n"; return 2; }
    using namespace nifly;
    NifFile nif;
    if (nif.Load(argv[2]) != 0) { std::cerr << "RESULT: fix FAILED - cannot load\n"; return 1; }

    std::vector<std::string> did;
    for (auto* s : nif.GetShapes()) {
        std::vector<Vector3> tang;
        bool hasTan = nif.GetTangentsForShape(s, tang) && !tang.empty();
        std::vector<Vector2> uvs;
        if (!hasTan && nif.GetUvsForShape(s, uvs) && !uvs.empty()) {
            nif.CalcTangentsForShape(s);
            did.push_back("recomputed tangents on '" + s->name.get() + "'");
        }
        s->UpdateBounds();
    }

    bool hasBsx = false;
    for (uint32_t i = 0; i < nif.GetHeader().GetNumBlocks(); ++i)
        if (nif.GetHeader().GetBlockTypeStringById(i) == "BSXFlags") hasBsx = true;
    if (!hasBsx) {
        auto bsx = std::make_unique<BSXFlags>();
        bsx->name = NiStringRef("BSX");
        bsx->integerData = 0;
        uint32_t id = nif.GetHeader().AddBlock(std::move(bsx));
        if (auto* root = nif.GetRootNode()) root->extraDataRefs.AddBlockRef(id);
        did.push_back("added missing BSXFlags");
    }

    nif.TrimTexturePaths();
    nif.FixShaderFlags();
    nif.FixBSXFlags();
    did.push_back("trimmed texture paths + fixed shader/BSX flags");

    NifSaveOptions opt;
    if (nif.Save(argv[3], opt) != 0) { std::cerr << "RESULT: fix FAILED - save error\n"; return 1; }

    std::cout << "RESULT: fix OK -> " << argv[3] << " (" << did.size() << " actions)\n";
    for (auto& d : did) std::cout << "  - " << d << "\n";
    return 0;
}

njson inspectJson(nifly::NifFile& nif) {
    using namespace nifly;
    njson j;
    j["fo4"] = nif.GetHeader().GetVersion().IsFO4();
    njson shapes = njson::array();
    NiHeader& hdr = nif.GetHeader();

    for (auto* s : nif.GetShapes()) {
        njson sj;
        sj["name"] = s->name.get();
        sj["verts"] = s->GetNumVertices();
        sj["tris"] = s->GetNumTriangles();
        sj["skinned"] = s->IsSkinned();
        std::string shaderType = "none";
        if (auto* sh = nif.GetShader(s)) shaderType = hdr.GetBlockTypeStringById(nif.GetBlockID(sh));
        sj["shader"] = shaderType;
        njson tex = njson::array();
        for (uint32_t slot = 0; slot < 8; ++slot) {
            std::string t;
            nif.GetTextureSlot(s, t, slot);
            if (!t.empty()) tex.push_back(njson{{"slot", slot}, {"path", t}});
        }
        sj["textures"] = tex;
        std::vector<Vector3> tang;
        sj["hasTangents"] = nif.GetTangentsForShape(s, tang) && !tang.empty();
        shapes.push_back(sj);
    }
    j["shapes"] = shapes;

    bool bsx = false, collision = false;
    for (uint32_t i = 0; i < hdr.GetNumBlocks(); ++i) {
        std::string t = hdr.GetBlockTypeStringById(i);
        if (t == "BSXFlags") bsx = true;
        if (t.rfind("bhk", 0) == 0 && t.find("CollisionObject") != std::string::npos) collision = true;
    }
    j["hasBSX"] = bsx;
    j["hasCollision"] = collision;
    return j;
}

int billboardModeForShape(nifly::NifFile& nif, nifly::NiShape* s) {
    using namespace nifly;
    NiObject* cur = s;
    for (size_t guard = 0, n = nif.GetHeader().GetNumBlocks(); guard <= n && cur; ++guard) {
        NiNode* parent = nif.GetParentNode(cur);
        if (!parent) break;
        if (auto* bb = dynamic_cast<NiBillboardNode*>(parent))
            return static_cast<int>(bb->billboardMode);
        cur = parent;
    }
    return -1;
}

nifly::MatTransform shapeTransformToGlobal(nifly::NifFile& nif, nifly::NiShape* s) {
    using namespace nifly;
    NiNode* root = nif.GetRootNode();
    MatTransform xform = s->GetTransformToParent();
    NiObject* cur = s;
    for (size_t guard = 0, n = nif.GetHeader().GetNumBlocks(); guard <= n; ++guard) {
        NiNode* parent = nif.GetParentNode(cur);
        if (!parent) break;
        if (parent != root)  // the engine replaces the root node's transform with the ref's, never composes it
            xform = parent->GetTransformToParent().ComposeTransforms(xform);
        cur = parent;
    }
    return xform;
}

njson matTransformJson(const nifly::MatTransform& t) {
    njson j;
    j["translation"] = {t.translation.x, t.translation.y, t.translation.z};
    j["rotation"] = {t.rotation[0].x, t.rotation[0].y, t.rotation[0].z,
                     t.rotation[1].x, t.rotation[1].y, t.rotation[1].z,
                     t.rotation[2].x, t.rotation[2].y, t.rotation[2].z};
    j["scale"] = t.scale;
    return j;
}

njson alphaJson(nifly::NifFile& nif, nifly::NiShape* s) {
    njson a;
    if (auto* ap = nif.GetAlphaProperty(s)) {
        a["blend"] = (ap->flags & 0x0001) != 0;
        a["srcBlend"] = (ap->flags >> 1) & 0xF;
        a["dstBlend"] = (ap->flags >> 5) & 0xF;
        a["test"] = (ap->flags >> 9) & 1;
        a["threshold"] = ap->threshold;
        a["flags"] = ap->flags;
    } else {
        a["blend"] = false;
        a["test"] = false;
    }
    return a;
}

njson lightingJson(nifly::BSLightingShaderProperty* lsp) {
    njson l;
    l["shaderType"] = lsp->GetShaderType();
    l["shaderFlags1"] = lsp->shaderFlags1;
    l["shaderFlags2"] = lsp->shaderFlags2;
    l["uvOffset"] = {lsp->uvOffset.u, lsp->uvOffset.v};
    l["uvScale"] = {lsp->uvScale.u, lsp->uvScale.v};
    l["emissiveColor"] = {lsp->emissiveColor.x, lsp->emissiveColor.y, lsp->emissiveColor.z};
    l["emissiveMultiple"] = lsp->emissiveMultiple;
    l["lumEmittance"] = lsp->lumEmittance;
    l["alpha"] = lsp->alpha;
    l["glossiness"] = lsp->glossiness;
    l["specularColor"] = {lsp->specularColor.x, lsp->specularColor.y, lsp->specularColor.z};
    l["specularStrength"] = lsp->specularStrength;
    l["environmentMapScale"] = lsp->environmentMapScale;
    l["softlighting"] = lsp->softlighting;
    l["rimlightPower"] = lsp->rimlightPower;
    l["subsurfaceRolloff"] = lsp->subsurfaceRolloff;
    l["backlightPower"] = lsp->backlightPower;
    l["fresnelPower"] = lsp->fresnelPower;
    l["grayscaleToPaletteScale"] = lsp->grayscaleToPaletteScale;
    l["textureClampMode"] = static_cast<uint32_t>(lsp->textureClampMode);
    l["doTranslucency"] = lsp->doTranslucency;
    l["subsurfaceColor"] = {lsp->subsurfaceColor.r, lsp->subsurfaceColor.g, lsp->subsurfaceColor.b};
    l["transmissiveScale"] = lsp->transmissiveScale;
    l["skinTintColor"] = {lsp->skinTintColor.x, lsp->skinTintColor.y, lsp->skinTintColor.z};
    l["hairTintColor"] = {lsp->hairTintColor.x, lsp->hairTintColor.y, lsp->hairTintColor.z};
    return l;
}

njson skinJson(nifly::NifFile& nif, nifly::NiShape* s) {
    using namespace nifly;
    njson sk;
    std::vector<std::string> boneNames;
    nif.GetShapeBoneList(s, boneNames);
    if (boneNames.empty()) return sk;
    sk["bones"] = boneNames;

    const uint32_t vertCount = s->GetNumVertices();
    std::vector<std::vector<std::pair<uint16_t, float>>> perVert(vertCount);
    for (uint32_t b = 0; b < boneNames.size(); ++b) {
        std::unordered_map<uint16_t, float> weights;
        nif.GetShapeBoneWeights(s, b, weights);
        for (const auto& [vi, w] : weights)
            if (vi < vertCount && w > 0.0f) perVert[vi].push_back({static_cast<uint16_t>(b), w});
    }

    njson idx = njson::array(), wts = njson::array();
    for (auto& infl : perVert) {
        std::sort(infl.begin(), infl.end(), [](auto& a, auto& b) { return a.second > b.second; });
        if (infl.size() > 4) infl.resize(4);
        float sum = 0.0f;
        for (auto& [bi, w] : infl) sum += w;
        for (size_t k = 0; k < 4; ++k) {
            if (k < infl.size()) {
                idx.push_back(infl[k].first);
                wts.push_back(sum > 0.0f ? infl[k].second / sum : 0.0f);
            } else {
                idx.push_back(0);
                wts.push_back(0.0f);
            }
        }
    }
    sk["boneIndices"] = idx;
    sk["boneWeights"] = wts;

    njson skinToBone = njson::array();
    for (uint32_t b = 0; b < boneNames.size(); ++b) {
        MatTransform t;
        if (nif.GetShapeTransformSkinToBone(s, b, t)) skinToBone.push_back(matTransformJson(t));
        else skinToBone.push_back(nullptr);
    }
    sk["skinToBone"] = skinToBone;

    MatTransform globalToSkin;
    if (nif.CalcShapeTransformGlobalToSkin(s, globalToSkin))
        sk["globalToSkin"] = matTransformJson(globalToSkin);
    return sk;
}

std::string base64Encode(const char* data, size_t len) {
    static const char tbl[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    std::string out;
    out.reserve(((len + 2) / 3) * 4);
    for (size_t i = 0; i < len; i += 3) {
        uint32_t n = static_cast<uint8_t>(data[i]) << 16;
        if (i + 1 < len) n |= static_cast<uint8_t>(data[i + 1]) << 8;
        if (i + 2 < len) n |= static_cast<uint8_t>(data[i + 2]);
        out.push_back(tbl[(n >> 18) & 63]);
        out.push_back(tbl[(n >> 12) & 63]);
        out.push_back(i + 1 < len ? tbl[(n >> 6) & 63] : '=');
        out.push_back(i + 2 < len ? tbl[n & 63] : '=');
    }
    return out;
}

// Summed translation from the NIF root down to `nodeId`, in NIF units. Rotations on
// intermediate nodes are intentionally ignored: FO4 collision-owning NiNode placements
// are translation-only in practice (see docs/FO4_HAVOK_PACKFILE_FORMAT.md provenance).
njson nodeChainTranslationJson(nifly::NifFile& nif, uint32_t nodeId) {
    using namespace nifly;
    float tx = 0.f, ty = 0.f, tz = 0.f;
    NiObject* cur = nif.GetHeader().GetBlock<NiAVObject>(nodeId);
    if (auto* av = dynamic_cast<NiAVObject*>(cur)) {
        auto t = av->GetTransformToParent().translation;
        tx += t.x; ty += t.y; tz += t.z;
    }
    for (size_t guard = 0, n = nif.GetHeader().GetNumBlocks(); guard <= n && cur; ++guard) {
        NiNode* parent = nif.GetParentNode(cur);
        if (!parent) break;
        auto t = parent->GetTransformToParent().translation;
        tx += t.x; ty += t.y; tz += t.z;
        cur = parent;
    }
    return njson::array({tx, ty, tz});
}

// FO4 collision: bhkNPCollisionObject -> { bodyID, bodyRef -> bhkPhysicsSystem (raw Havok
// 2014.1.0-r1 packfile), targetRef -> owning node }. Emits each distinct system blob once
// (base64) plus per-collision-object node translations; null when the NIF has none.
njson havokPackedJson(nifly::NifFile& nif) {
    using namespace nifly;
    auto& hdr = nif.GetHeader();
    njson systems = njson::array();
    njson collisionObjects = njson::array();
    std::set<uint32_t> emittedSystems;

    for (uint32_t i = 0; i < hdr.GetNumBlocks(); ++i) {
        if (hdr.GetBlockTypeStringById(i) != "bhkNPCollisionObject") continue;
        auto* co = hdr.GetBlock<bhkNPCollisionObject>(i);
        if (!co) continue;
        uint32_t sysId = co->bodyRef.index;
        auto* ps = hdr.GetBlock<bhkPhysicsSystem>(sysId);
        if (!ps || ps->data.size() == 0) continue;

        if (emittedSystems.insert(sysId).second) {
            std::string bytes(ps->data.begin(), ps->data.end());
            systems.push_back(njson{{"id", sysId}, {"dataBase64", base64Encode(bytes.data(), bytes.size())}});
        }
        collisionObjects.push_back(njson{
            {"nodeId", co->targetRef.index},
            {"bodyId", co->bodyID},
            {"systemId", sysId},
            {"nodeTranslation", nodeChainTranslationJson(nif, co->targetRef.index)},
        });
    }

    if (collisionObjects.empty()) return njson();
    njson j;
    j["havokScale"] = 0.0142875f;
    j["scheme"] = "havokPacked";
    j["systems"] = systems;
    j["collisionObjects"] = collisionObjects;
    return j;
}

int cmdGeo(int argc, char** argv) {
    if (argc < 3) { std::cerr << "usage: niftool geo <nif> [--skin] [--collision]\n"; return 2; }
    using namespace nifly;
    bool wantSkin = false, wantCollision = false;
    for (int i = 3; i < argc; ++i) {
        if (std::string(argv[i]) == "--skin") wantSkin = true;
        if (std::string(argv[i]) == "--collision") wantCollision = true;
    }

    NifFile nif;
    if (nif.Load(argv[2]) != 0) { std::cerr << "{\"error\":\"cannot load NIF\"}\n"; return 1; }

    njson j;
    j["fo4"] = nif.GetHeader().GetVersion().IsFO4();
    njson shapes = njson::array();
    for (auto* s : nif.GetShapes()) {
        njson sj;
        sj["name"] = s->name.get();

        int bbMode = billboardModeForShape(nif, s);
        if (bbMode >= 0) { sj["billboard"] = true; sj["billboardMode"] = bbMode; }

        std::vector<Vector3> verts;
        nif.GetVertsForShape(s, verts);
        njson vj = njson::array();
        for (const auto& v : verts) { vj.push_back(v.x); vj.push_back(v.y); vj.push_back(v.z); }
        sj["verts"] = vj;

        std::vector<Triangle> tris;
        s->GetTriangles(tris);
        njson tj = njson::array();
        for (const auto& t : tris) { tj.push_back(t.p1); tj.push_back(t.p2); tj.push_back(t.p3); }
        sj["tris"] = tj;

        njson nj = njson::array();
        if (auto* norms = nif.GetNormalsForShape(s))
            for (const auto& n : *norms) { nj.push_back(n.x); nj.push_back(n.y); nj.push_back(n.z); }
        sj["normals"] = nj;

        std::vector<Vector2> uvs;
        njson uj = njson::array();
        if (nif.GetUvsForShape(s, uvs))
            for (const auto& u : uvs) { uj.push_back(u.u); uj.push_back(u.v); }
        sj["uvs"] = uj;

        std::vector<Vector3> tangs;
        if (nif.GetTangentsForShape(s, tangs) && !tangs.empty()) {
            njson gj = njson::array();
            for (const auto& t : tangs) { gj.push_back(t.x); gj.push_back(t.y); gj.push_back(t.z); }
            sj["tangents"] = gj;
        }
        std::vector<Vector3> bitangs;
        if (nif.GetBitangentsForShape(s, bitangs) && !bitangs.empty()) {
            njson bj = njson::array();
            for (const auto& b : bitangs) { bj.push_back(b.x); bj.push_back(b.y); bj.push_back(b.z); }
            sj["bitangents"] = bj;
        }

        sj["transform"] = matTransformJson(shapeTransformToGlobal(nif, s));
        sj["alpha"] = alphaJson(nif, s);

        njson tex = njson::array();
        for (uint32_t slot = 0; slot < 8; ++slot) {
            std::string t;
            nif.GetTextureSlot(s, t, slot);
            if (!t.empty()) tex.push_back(njson{{"slot", slot}, {"path", t}});
        }
        sj["textures"] = tex;

        if (auto* lsp = dynamic_cast<BSLightingShaderProperty*>(nif.GetShader(s))) {
            const std::string& wet = lsp->rootMaterialName.get();
            const std::string& name = lsp->name.get();
            const bool ownBgsm = name.size() > 5 && std::equal(name.end() - 5, name.end(), ".bgsm",
                [](unsigned char a, char b) { return std::tolower(a) == b; });
            if (!wet.empty()) sj["bgsmPath"] = wet;
            else if (ownBgsm) sj["bgsmPath"] = name;
            sj["materialPath"] = ownBgsm ? name : std::string();
            sj["lighting"] = lightingJson(lsp);
        }

        sj["skinned"] = s->IsSkinned();
        if (wantSkin && s->IsSkinned()) {
            njson sk = skinJson(nif, s);
            if (!sk.is_null() && !sk.empty()) sj["skin"] = sk;
        }

        auto* esp = dynamic_cast<BSEffectShaderProperty*>(nif.GetShader(s));
        sj["effectShader"] = esp != nullptr;
        if (esp) {

            njson ej;
            ej["baseColor"] = {esp->baseColor.r, esp->baseColor.g, esp->baseColor.b, esp->baseColor.a};
            ej["baseColorScale"] = esp->baseColorScale;
            Color4 emissive = esp->GetEmissiveColor();
            ej["emissiveColor"] = {emissive.r, emissive.g, emissive.b};
            ej["emissiveMultiple"] = esp->GetEmissiveMultiple();
            ej["useFalloff"] = (esp->shaderFlags1 & SLSF1_USE_FALLOFF) != 0;
            ej["falloffStartAngle"] = esp->falloffStartAngle;
            ej["falloffStopAngle"] = esp->falloffStopAngle;
            ej["falloffStartOpacity"] = esp->falloffStartOpacity;
            ej["falloffStopOpacity"] = esp->falloffStopOpacity;
            ej["softFalloffDepth"] = esp->softFalloffDepth;
            ej["vertexAlpha"] = (esp->shaderFlags1 & SLSF1_VERTEX_ALPHA) != 0;
            ej["shaderFlags1"] = esp->shaderFlags1;
            ej["shaderFlags2"] = esp->shaderFlags2;
            ej["uvOffset"] = {esp->uvOffset.u, esp->uvOffset.v};
            ej["uvScale"] = {esp->uvScale.u, esp->uvScale.v};
            ej["envMapScale"] = esp->envMapScale;
            ej["lumEmittance"] = esp->lumEmittance;
            ej["textureClampMode"] = esp->textureClampMode;

            ej["sourceTexture"] = esp->sourceTexture.get();
            ej["greyscaleTexture"] = esp->greyscaleTexture.get();
            ej["normalTexture"] = esp->normalTexture.get();
            ej["envMapTexture"] = esp->envMapTexture.get();
            ej["envMaskTexture"] = esp->envMaskTexture.get();
            ej["lightingTexture"] = esp->lightingTexture.get();
            ej["materialName"] = esp->name.get();

            if (auto* ap = nif.GetAlphaProperty(s)) {
                ej["alphaBlend"] = (ap->flags & 0x0001) != 0;
                ej["alphaSrcBlend"] = (ap->flags >> 1) & 0xF;
                ej["alphaDstBlend"] = (ap->flags >> 5) & 0xF;
            } else {
                ej["alphaBlend"] = false;
            }
            sj["effect"] = ej;
        }

        std::vector<Color4> vcolors;
        if (nif.GetColorsForShape(s, vcolors) && !vcolors.empty()) {
            njson vcj = njson::array();
            for (const auto& c : vcolors) { vcj.push_back(c.r); vcj.push_back(c.g); vcj.push_back(c.b); vcj.push_back(c.a); }
            sj["vertexColors"] = vcj;
        }

        shapes.push_back(sj);
    }
    j["shapes"] = shapes;
    if (wantCollision) {
        njson col = havokPackedJson(nif);
        j["collision"] = col.is_null() ? njson{{"scheme", "none"}} : col;
    }
    std::cout << j.dump() << "\n";
    return 0;
}

njson havokShapeJson(nifly::NifFile& nif, uint32_t shapeId, int depth);

njson havokShapeJson(nifly::NifFile& nif, uint32_t shapeId, int depth) {
    using namespace nifly;
    njson j;
    if (depth > 8) { j["type"] = "tooDeep"; return j; }
    auto& hdr = nif.GetHeader();
    auto* shape = hdr.GetBlock<bhkShape>(shapeId);
    if (!shape) return j;
    j["type"] = hdr.GetBlockTypeStringById(shapeId);
    j["material"] = static_cast<uint32_t>(shape->GetMaterial());

    if (auto* box = dynamic_cast<bhkBoxShape*>(shape)) {
        j["dimensions"] = {box->dimensions.x, box->dimensions.y, box->dimensions.z};
        j["radius"] = box->radius;
    } else if (auto* sph = dynamic_cast<bhkSphereShape*>(shape)) {
        j["radius"] = sph->radius;
    } else if (auto* cap = dynamic_cast<bhkCapsuleShape*>(shape)) {
        j["point1"] = {cap->point1.x, cap->point1.y, cap->point1.z};
        j["point2"] = {cap->point2.x, cap->point2.y, cap->point2.z};
        j["radius1"] = cap->radius1;
        j["radius2"] = cap->radius2;
    } else if (auto* cvx = dynamic_cast<bhkConvexVerticesShape*>(shape)) {
        njson vj = njson::array();
        for (const auto& v : cvx->verts) { vj.push_back(v.x); vj.push_back(v.y); vj.push_back(v.z); }
        j["verts"] = vj;
        j["radius"] = cvx->radius;
    } else if (auto* xf = dynamic_cast<bhkTransformShape*>(shape)) {
        njson m = njson::array();
        for (int e = 0; e < 16; ++e) m.push_back(xf->xform[e]);
        j["xform"] = m;
        j["child"] = havokShapeJson(nif, xf->shapeRef.index, depth + 1);
    } else if (auto* mopp = dynamic_cast<bhkMoppBvTreeShape*>(shape)) {
        j["child"] = havokShapeJson(nif, mopp->shapeRef.index, depth + 1);
    } else if (auto* list = dynamic_cast<bhkListShape*>(shape)) {
        njson kids = njson::array();
        for (auto& ref : list->subShapeRefs)
            kids.push_back(havokShapeJson(nif, ref.index, depth + 1));
        j["children"] = kids;
    } else if (auto* strips = dynamic_cast<bhkNiTriStripsShape*>(shape)) {
        j["scale"] = {strips->scale.x, strips->scale.y, strips->scale.z};
        njson parts = njson::array();
        for (auto& ref : strips->partRefs) {
            auto* data = hdr.GetBlock<NiTriStripsData>(ref.index);
            if (!data) continue;
            njson p;
            njson vj = njson::array();
            for (const auto& v : data->vertices) { vj.push_back(v.x); vj.push_back(v.y); vj.push_back(v.z); }
            p["verts"] = vj;
            std::vector<Triangle> tris;
            data->GetTriangles(tris);
            njson tj = njson::array();
            for (const auto& t : tris) { tj.push_back(t.p1); tj.push_back(t.p2); tj.push_back(t.p3); }
            p["tris"] = tj;
            parts.push_back(p);
        }
        j["parts"] = parts;
    }
    return j;
}

int cmdCollision(int argc, char** argv) {
    if (argc < 3) { std::cerr << "usage: niftool collision <nif> [--dump-packed <dir>]\n"; return 2; }
    const std::string dumpDir = argVal(argc, argv, "--dump-packed");
    using namespace nifly;
    NifFile nif;
    if (nif.Load(argv[2]) != 0) { std::cerr << "{\"error\":\"cannot load NIF\"}\n"; return 1; }

    auto& hdr = nif.GetHeader();
    njson j;

    j["havokScale"] = 0.0142875f;
    j["scheme"] = "none";

    njson bodies = njson::array();
    for (uint32_t i = 0; i < hdr.GetNumBlocks(); ++i) {
        const std::string type = hdr.GetBlockTypeStringById(i);
        if (type == "bhkPhysicsSystem" || type == "bhkRagdollSystem") {
            auto* sys = hdr.GetBlock<bhkPhysicsSystem>(i);
            if (sys && !sys->data.empty() && !dumpDir.empty()) {
                const std::string out = dumpDir + "/packed_" + std::to_string(i) + ".hkx";
                std::ofstream fs(out, std::ios::binary);
                fs.write(reinterpret_cast<const char*>(sys->data.data()),
                         static_cast<std::streamsize>(sys->data.size()));
            }
            continue;
        }
        auto* body = hdr.GetBlock<bhkRigidBody>(i);
        if (!body) continue;
        njson b;
        b["block"] = type;
        b["translation"] = {body->translation.x, body->translation.y, body->translation.z};
        b["rotation"] = {body->rotation.x, body->rotation.y, body->rotation.z, body->rotation.w};
        b["layer"] = static_cast<uint32_t>(body->collisionFilter.layer);
        b["motionSystem"] = body->motionSystem;
        b["mass"] = body->mass;
        b["shape"] = havokShapeJson(nif, body->shapeRef.index, 0);
        bodies.push_back(b);
    }

    njson packed = havokPackedJson(nif);
    if (!bodies.empty()) { j["scheme"] = "bhkShapes"; j["bodies"] = bodies; }
    else if (!packed.is_null()) j = packed;

    std::cout << j.dump() << "\n";
    return 0;
}

const char* texSlotLabel(uint32_t s) {
    switch (s) {
        case 0: return "Diffuse";
        case 1: return "Normal";
        case 2: return "Specular / Glow";
        case 3: return "Greyscale";
        case 4: return "Cubemap (Env)";
        case 5: return "Env Mask";
        case 6: return "Tint / Subsurface";
        case 7: return "Smoothness / Specular";
        default: return "Slot";
    }
}

njson mkField(const std::string& key, const std::string& label, const std::string& type, njson value) {
    return njson{{"key", key}, {"label", label}, {"type", type}, {"value", value}};
}

struct FlagDef { const char* key; const char* label; int word; uint32_t bit; };
const FlagDef kShaderFlags[] = {
    {"sf1_specular",           "Specular",            1, nifly::SLSF1_SPECULAR},
    {"sf1_vertex_alpha",       "Vertex Alpha",        1, nifly::SLSF1_VERTEX_ALPHA},
    {"sf1_greyscale_color",    "Greyscale to Color",  1, nifly::SLSF1_GREYSCALETOPALETTE_COLOR},
    {"sf1_use_falloff",        "Use Falloff",         1, nifly::SLSF1_USE_FALLOFF},
    {"sf1_environment_mapping","Environment Map",     1, nifly::SLSF1_ENVIRONMENT_MAPPING},
    {"sf1_receive_shadows",    "Receive Shadows",     1, nifly::SLSF1_RECEIVE_SHADOWS},
    {"sf1_cast_shadows",       "Cast Shadows",        1, nifly::SLSF1_CAST_SHADOWS},
    {"sf1_model_space_normals","Model Space Normals", 1, nifly::SLSF1_MODEL_SPACE_NORMALS},
    {"sf1_own_emit",           "Own Emit",            1, nifly::SLSF1_OWN_EMIT},
    {"sf1_decal",              "Decal",               1, nifly::SLSF1_DECAL},
    {"sf1_dynamic_decal",      "Dynamic Decal",       1, nifly::SLSF1_DYNAMIC_DECAL},
    {"sf1_external_emittance", "External Emittance",  1, nifly::SLSF1_EXTERNAL_EMITTANCE},
    {"sf1_zbuffer_test",       "Z-Buffer Test",       1, nifly::SLSF1_ZBUFFER_TEST},
    {"sf2_zbuffer_write",      "Z-Buffer Write",      2, nifly::SLSF2_ZBUFFER_WRITE},
    {"sf2_double_sided",       "Double Sided",        2, nifly::SLSF2_DOUBLE_SIDED},
    {"sf2_vertex_colors",      "Vertex Colors",       2, nifly::SLSF2_VERTEX_COLORS},
    {"sf2_glow_map",           "Glow Map",            2, nifly::SLSF2_GLOW_MAP},
    {"sf2_tree_anim",          "Tree Anim",           2, nifly::SLSF2_TREE_ANIM},
    {"sf2_effect_lighting",    "Effect Lighting",     2, nifly::SLSF2_EFFECT_LIGHTING},
};

njson shaderFlagsValue(nifly::BSShaderProperty* bsp) {
    njson arr = njson::array();
    for (const auto& f : kShaderFlags) {
        bool on = (f.word == 1 ? bsp->shaderFlags1 : bsp->shaderFlags2) & f.bit;
        arr.push_back(njson{{"key", f.key}, {"label", f.label}, {"on", on}});
    }
    return arr;
}

void applyShaderFlags(nifly::BSShaderProperty* bsp, const njson& value) {
    for (const auto& item : value) {
        std::string key = item.value("key", "");
        bool on = item.value("on", false);
        for (const auto& f : kShaderFlags) {
            if (key != f.key) continue;
            uint32_t& word = (f.word == 1) ? bsp->shaderFlags1 : bsp->shaderFlags2;
            if (on) word |= f.bit; else word &= ~f.bit;
            break;
        }
    }
}

int cmdTree(int argc, char** argv) {
    if (argc < 3) { std::cerr << "usage: niftool tree <nif>\n"; return 2; }
    using namespace nifly;
    NifFile nif;
    if (nif.Load(argv[2]) != 0) { std::cout << "{\"error\":\"cannot load NIF\"}\n"; return 1; }

    NiHeader& hdr = nif.GetHeader();
    njson j;
    j["fo4"] = hdr.GetVersion().IsFO4();
    j["file"] = fs::path(argv[2]).filename().string();
    njson blocks = njson::array();

    for (auto* n : nif.GetNodes()) {
        if (!n) continue;
        const MatTransform& t = n->GetTransformToParent();
        njson fields = njson::array();
        fields.push_back(mkField("name", "Name", "string", n->name.get()));
        fields.push_back(mkField("translation", "Translation", "vec3",
                                 njson{t.translation.x, t.translation.y, t.translation.z}));
        fields.push_back(mkField("scale", "Scale", "float", t.scale));
        fields.push_back(mkField("rotation", "Rotation(3x3)", "readonly",
                                 njson{t.rotation[0].x, t.rotation[0].y, t.rotation[0].z,
                                       t.rotation[1].x, t.rotation[1].y, t.rotation[1].z,
                                       t.rotation[2].x, t.rotation[2].y, t.rotation[2].z}));
        blocks.push_back(njson{{"id", nif.GetBlockID(n)}, {"type", n->GetBlockName()},
                               {"name", n->name.get()}, {"group", "Nodes"}, {"cat", "node"},
                               {"fields", fields}});
    }

    for (auto* s : nif.GetShapes()) {
        if (!s) continue;
        const MatTransform& t = s->GetTransformToParent();
        njson fields = njson::array();
        fields.push_back(mkField("name", "Name", "string", s->name.get()));
        fields.push_back(mkField("verts", "Vertices", "readonly", s->GetNumVertices()));
        fields.push_back(mkField("tris", "Triangles", "readonly", s->GetNumTriangles()));
        fields.push_back(mkField("translation", "Translation", "vec3",
                                 njson{t.translation.x, t.translation.y, t.translation.z}));
        fields.push_back(mkField("scale", "Scale", "float", t.scale));

        auto* shader = nif.GetShader(s);
        auto* lsp = dynamic_cast<BSLightingShaderProperty*>(shader);
        auto* esp = dynamic_cast<BSEffectShaderProperty*>(shader);
        if (shader)
            fields.push_back(mkField("shaderType", "Shader", "readonly",
                                     hdr.GetBlockTypeStringById(nif.GetBlockID(shader))));
        if (lsp) {
            fields.push_back(mkField("material", "Material (.bgsm)", "string", lsp->rootMaterialName.get()));
            fields.push_back(mkField("emissiveColor", "Emissive Color", "color",
                                     njson{lsp->emissiveColor.x, lsp->emissiveColor.y, lsp->emissiveColor.z}));
            fields.push_back(mkField("emissiveMultiple", "Emissive Multiplier", "float", lsp->emissiveMultiple));
            fields.push_back(mkField("specularColor", "Specular Color", "color",
                                     njson{lsp->specularColor.x, lsp->specularColor.y, lsp->specularColor.z}));
            fields.push_back(mkField("specularStrength", "Specular Strength", "float", lsp->specularStrength));
            fields.push_back(mkField("glossiness", "Glossiness", "float", lsp->glossiness));
            fields.push_back(mkField("alpha", "Opacity (shader)", "float", lsp->alpha));
            fields.push_back(mkField("uvOffset", "UV Offset", "vec2", njson{lsp->uvOffset.u, lsp->uvOffset.v}));
            fields.push_back(mkField("uvScale", "UV Scale", "vec2", njson{lsp->uvScale.u, lsp->uvScale.v}));
        }
        if (esp) {
            fields.push_back(mkField("eff_baseColor", "Base Color", "color",
                                     njson{esp->baseColor.r, esp->baseColor.g, esp->baseColor.b}));
            fields.push_back(mkField("eff_baseColorScale", "Base Color Scale", "float", esp->baseColorScale));
            fields.push_back(mkField("eff_emittanceColor", "Emittance Color", "color",
                                     njson{esp->emittanceColor.r, esp->emittanceColor.g, esp->emittanceColor.b}));
            fields.push_back(mkField("eff_envMapScale", "Env Map Scale", "float", esp->envMapScale));
            fields.push_back(mkField("eff_falloffStartOpacity", "Falloff Start Opacity", "float", esp->falloffStartOpacity));
            fields.push_back(mkField("eff_falloffStopOpacity", "Falloff Stop Opacity", "float", esp->falloffStopOpacity));
            fields.push_back(mkField("eff_softFalloffDepth", "Soft Falloff Depth", "float", esp->softFalloffDepth));
            fields.push_back(mkField("eff_sourceTexture", "Source Texture", "tex", esp->sourceTexture.get()));
            fields.push_back(mkField("eff_greyscaleTexture", "Greyscale Texture", "tex", esp->greyscaleTexture.get()));
            fields.push_back(mkField("eff_normalTexture", "Normal Texture", "tex", esp->normalTexture.get()));
        }

        if (auto* bsp = dynamic_cast<BSShaderProperty*>(shader))
            fields.push_back(mkField("shaderFlags", "Shader Flags", "flags", shaderFlagsValue(bsp)));

        auto* alpha = nif.GetAlphaProperty(s);
        uint16_t aflags = alpha ? alpha->flags : 0;
        fields.push_back(mkField("alphaBlend", "Alpha Blend", "bool", (aflags & 0x0001) != 0));
        fields.push_back(mkField("alphaTest", "Alpha Test", "bool", (aflags & 0x0200) != 0));
        fields.push_back(mkField("alphaThreshold", "Alpha Threshold (0-255)", "int",
                                 alpha ? static_cast<int>(alpha->threshold) : 128));

        for (uint32_t slot = 0; slot < 8; ++slot) {
            std::string tex;
            nif.GetTextureSlot(s, tex, slot);
            fields.push_back(mkField("tex" + std::to_string(slot),
                                     std::string(texSlotLabel(slot)) + " (slot " + std::to_string(slot) + ")",
                                     "tex", tex));
        }

        blocks.push_back(njson{{"id", nif.GetBlockID(s)}, {"type", s->GetBlockName()},
                               {"name", s->name.get()}, {"group", "Shapes"}, {"cat", "shape"},
                               {"fields", fields}});
    }

    if (auto* root = nif.GetRootNode()) {
        if (auto* col = hdr.GetBlock<bhkCollisionObject>(root->collisionRef.index)) {
            uint32_t bodyId = col->bodyRef.index;
            if (auto* body = hdr.GetBlock<bhkRigidBody>(bodyId)) {
                njson fields = njson::array();
                fields.push_back(mkField("layer", "Collision Layer (1=static)", "int",
                                         static_cast<int>(body->collisionFilter.layer)));
                if (auto* box = hdr.GetBlock<bhkBoxShape>(body->shapeRef.index)) {
                    fields.push_back(mkField("boxDimensions", "Box Half-Extents (Havok units)", "vec3",
                                             njson{box->dimensions.x, box->dimensions.y, box->dimensions.z}));
                    fields.push_back(mkField("boxRadius", "Box Radius", "float", box->radius));
                    fields.push_back(mkField("material", "Havok Material", "int",
                                             static_cast<int>(box->GetMaterial())));
                }
                blocks.push_back(njson{{"id", bodyId}, {"type", hdr.GetBlockTypeStringById(bodyId)},
                                       {"name", "Box Collision"}, {"group", "Collision"}, {"cat", "collision"},
                                       {"fields", fields}});
            }
        }
    }

    for (uint32_t i = 0; i < hdr.GetNumBlocks(); ++i) {
        if (hdr.GetBlockTypeStringById(i) != "BSXFlags") continue;
        auto* bsx = hdr.GetBlock<BSXFlags>(i);
        if (!bsx) continue;
        njson fields = njson::array();
        fields.push_back(mkField("flags", "BSX Flags (bit1=Havok, bit0=Animated)", "int", bsx->integerData));
        blocks.push_back(njson{{"id", i}, {"type", "BSXFlags"}, {"name", bsx->name.get()},
                               {"group", "Extra"}, {"cat", "bsx"}, {"fields", fields}});
    }

    j["blocks"] = blocks;
    std::cout << j.dump() << "\n";
    return 0;
}

int cmdSet(int argc, char** argv) {
    if (argc < 5) { std::cerr << "RESULT: set FAILED - usage: niftool set <in.nif> <out.nif> <edits.json>\n"; return 2; }
    using namespace nifly;
    NifFile nif;
    if (nif.Load(argv[2]) != 0) { std::cerr << "RESULT: set FAILED - cannot load\n"; return 1; }

    njson edits;
    { std::ifstream in(argv[4]);
      if (!in) { std::cerr << "RESULT: set FAILED - cannot read edits file\n"; return 1; }
      try { in >> edits; } catch (const std::exception& e) {
          std::cerr << "RESULT: set FAILED - bad edits JSON: " << e.what() << "\n"; return 1; } }
    if (!edits.is_array()) { std::cerr << "RESULT: set FAILED - edits must be a JSON array\n"; return 1; }

    std::unordered_map<uint32_t, NiNode*> nodeById;
    for (auto* n : nif.GetNodes()) if (n) nodeById[nif.GetBlockID(n)] = n;
    std::unordered_map<uint32_t, NiShape*> shapeById;
    for (auto* s : nif.GetShapes()) if (s) shapeById[nif.GetBlockID(s)] = s;

    auto asFloat = [](const njson& v) { return v.is_number() ? v.get<float>() : std::stof(v.get<std::string>()); };
    int applied = 0;
    std::vector<std::string> problems;

    for (const auto& e : edits) {
        try {
            uint32_t id = e.at("id").get<uint32_t>();
            std::string cat = e.value("cat", "");
            std::string key = e.at("key").get<std::string>();
            const njson& val = e.at("value");

            if (cat == "bsx") {
                BSXFlags* bsx = nullptr;
                for (uint32_t i = 0; i < nif.GetHeader().GetNumBlocks(); ++i)
                    if (nif.GetHeader().GetBlockTypeStringById(i) == "BSXFlags") {
                        bsx = nif.GetHeader().GetBlock<BSXFlags>(i); break;
                    }
                if (!bsx) { problems.push_back("no BSXFlags block"); continue; }
                bsx->integerData = val.is_number() ? val.get<uint32_t>()
                                                   : static_cast<uint32_t>(std::stoul(val.get<std::string>()));
                ++applied;
            } else if (cat == "node") {
                auto it = nodeById.find(id);
                if (it == nodeById.end()) { problems.push_back("no node #" + std::to_string(id)); continue; }
                NiNode* n = it->second;
                if (key == "name") { n->name.get() = val.get<std::string>(); }
                else if (key == "translation") {
                    MatTransform t = n->GetTransformToParent();
                    t.translation = Vector3(asFloat(val[0]), asFloat(val[1]), asFloat(val[2]));
                    n->SetTransformToParent(t);
                } else if (key == "scale") {
                    MatTransform t = n->GetTransformToParent(); t.scale = asFloat(val); n->SetTransformToParent(t);
                } else if (key == "rotation") {
                    constexpr double kPi = 3.14159265358979323846;
                    double ax = asFloat(val[0]) * kPi / 180.0, ay = asFloat(val[1]) * kPi / 180.0, az = asFloat(val[2]) * kPi / 180.0;
                    double cx = std::cos(ax), sx = std::sin(ax), cy = std::cos(ay), sy = std::sin(ay), cz = std::cos(az), sz = std::sin(az);

                    Matrix3 m(
                        (float)(cz * cy), (float)(cz * sy * sx - sz * cx), (float)(cz * sy * cx + sz * sx),
                        (float)(sz * cy), (float)(sz * sy * sx + cz * cx), (float)(sz * sy * cx - cz * sx),
                        (float)(-sy),     (float)(cy * sx),                (float)(cy * cx));
                    MatTransform t = n->GetTransformToParent(); t.rotation = m; n->SetTransformToParent(t);
                } else { problems.push_back("node key '" + key + "'"); continue; }
                ++applied;
            } else if (cat == "shape") {
                auto it = shapeById.find(id);
                if (it == shapeById.end()) { problems.push_back("no shape #" + std::to_string(id)); continue; }
                NiShape* s = it->second;
                auto* lsp = dynamic_cast<BSLightingShaderProperty*>(nif.GetShader(s));
                if (key == "name") { NifFile::RenameShape(s, val.get<std::string>()); }
                else if (key == "translation") {
                    MatTransform t = s->GetTransformToParent();
                    t.translation = Vector3(asFloat(val[0]), asFloat(val[1]), asFloat(val[2]));
                    s->SetTransformToParent(t);
                } else if (key == "scale") {
                    MatTransform t = s->GetTransformToParent(); t.scale = asFloat(val); s->SetTransformToParent(t);
                } else if (key.rfind("tex", 0) == 0 && key.size() > 3 && std::isdigit((unsigned char)key[3])) {
                    uint32_t slot = static_cast<uint32_t>(std::stoul(key.substr(3)));
                    std::string tex = val.get<std::string>(); nif.SetTextureSlot(s, tex, slot);
                } else if (key == "shaderFlags") {
                    auto* bsp = dynamic_cast<BSShaderProperty*>(nif.GetShader(s));
                    if (!bsp) { problems.push_back("shape #" + std::to_string(id) + " has no shader"); continue; }
                    applyShaderFlags(bsp, val);
                } else if (key.rfind("eff_", 0) == 0) {
                    auto* esp = dynamic_cast<BSEffectShaderProperty*>(nif.GetShader(s));
                    if (!esp) { problems.push_back("shape #" + std::to_string(id) + " is not an effect shader"); continue; }
                    if (key == "eff_baseColor") { esp->baseColor.r = asFloat(val[0]); esp->baseColor.g = asFloat(val[1]); esp->baseColor.b = asFloat(val[2]); }
                    else if (key == "eff_baseColorScale") esp->baseColorScale = asFloat(val);
                    else if (key == "eff_emittanceColor") { esp->emittanceColor.r = asFloat(val[0]); esp->emittanceColor.g = asFloat(val[1]); esp->emittanceColor.b = asFloat(val[2]); }
                    else if (key == "eff_envMapScale") esp->envMapScale = asFloat(val);
                    else if (key == "eff_falloffStartOpacity") esp->falloffStartOpacity = asFloat(val);
                    else if (key == "eff_falloffStopOpacity") esp->falloffStopOpacity = asFloat(val);
                    else if (key == "eff_softFalloffDepth") esp->softFalloffDepth = asFloat(val);
                    else if (key == "eff_sourceTexture") esp->sourceTexture.get() = val.get<std::string>();
                    else if (key == "eff_greyscaleTexture") esp->greyscaleTexture.get() = val.get<std::string>();
                    else if (key == "eff_normalTexture") esp->normalTexture.get() = val.get<std::string>();
                    else { problems.push_back("effect key '" + key + "'"); continue; }
                } else if (key == "alphaBlend" || key == "alphaTest" || key == "alphaThreshold") {
                    auto* alpha = nif.GetAlphaProperty(s);
                    if (!alpha) { nif.AssignAlphaProperty(s, std::make_unique<NiAlphaProperty>()); alpha = nif.GetAlphaProperty(s); }
                    if (!alpha) { problems.push_back("could not create alpha property on shape #" + std::to_string(id)); continue; }
                    if (key == "alphaThreshold") {
                        int th = val.is_number() ? val.get<int>() : std::stoi(val.get<std::string>());
                        alpha->threshold = static_cast<uint8_t>(std::max(0, std::min(255, th)));
                    } else {
                        uint16_t bit = (key == "alphaBlend") ? 0x0001 : 0x0200;
                        if (val.get<bool>()) alpha->flags |= bit; else alpha->flags &= ~bit;
                    }
                } else if (lsp) {
                    if (key == "material") lsp->rootMaterialName = NiStringRef(val.get<std::string>());
                    else if (key == "emissiveColor") lsp->emissiveColor = Vector3(asFloat(val[0]), asFloat(val[1]), asFloat(val[2]));
                    else if (key == "emissiveMultiple") lsp->emissiveMultiple = asFloat(val);
                    else if (key == "specularColor") lsp->specularColor = Vector3(asFloat(val[0]), asFloat(val[1]), asFloat(val[2]));
                    else if (key == "specularStrength") lsp->specularStrength = asFloat(val);
                    else if (key == "glossiness") lsp->glossiness = asFloat(val);
                    else if (key == "alpha") lsp->alpha = asFloat(val);
                    else if (key == "uvOffset") lsp->uvOffset = Vector2(asFloat(val[0]), asFloat(val[1]));
                    else if (key == "uvScale") lsp->uvScale = Vector2(asFloat(val[0]), asFloat(val[1]));
                    else if (key == "doubleSided") lsp->SetDoubleSided(val.get<bool>());
                    else { problems.push_back("shape key '" + key + "'"); continue; }
                } else { problems.push_back("shape key '" + key + "' needs a lighting shader"); continue; }
                ++applied;
            } else if (cat == "collision") {
                auto* body = nif.GetHeader().GetBlock<bhkRigidBody>(id);
                if (!body) { problems.push_back("no rigid body #" + std::to_string(id)); continue; }
                if (key == "layer") {
                    body->collisionFilter.layer = static_cast<uint8_t>(val.is_number() ? val.get<int>() : std::stoi(val.get<std::string>()));
                    body->collisionFilterCopy = body->collisionFilter; ++applied;
                } else {
                    auto* box = nif.GetHeader().GetBlock<bhkBoxShape>(body->shapeRef.index);
                    if (!box) { problems.push_back("collision #" + std::to_string(id) + " has no box shape"); continue; }
                    if (key == "boxDimensions") { box->dimensions = Vector3(asFloat(val[0]), asFloat(val[1]), asFloat(val[2])); ++applied; }
                    else if (key == "boxRadius") { box->radius = asFloat(val); ++applied; }
                    else if (key == "material") { box->SetMaterial(val.is_number() ? val.get<uint32_t>() : static_cast<uint32_t>(std::stoul(val.get<std::string>()))); ++applied; }
                    else problems.push_back("collision key '" + key + "'");
                }
            } else if (cat == "bsx") {
                auto* bsx = nif.GetHeader().GetBlock<BSXFlags>(id);
                if (!bsx) { problems.push_back("no BSXFlags #" + std::to_string(id)); continue; }
                if (key == "flags") { bsx->integerData = val.is_number() ? val.get<uint32_t>()
                                                       : static_cast<uint32_t>(std::stoul(val.get<std::string>())); ++applied; }
                else { problems.push_back("bsx key '" + key + "'"); }
            } else { problems.push_back("unknown cat '" + cat + "'"); }
        } catch (const std::exception& ex) { problems.push_back(std::string("edit error: ") + ex.what()); }
    }

    NifSaveOptions opt;
    if (nif.Save(argv[3], opt) != 0) { std::cerr << "RESULT: set FAILED - save error\n"; return 1; }

    std::cout << "RESULT: set OK -> " << argv[3] << " (" << applied << " change(s) applied";
    if (!problems.empty()) std::cout << ", " << problems.size() << " skipped";
    std::cout << ")\n";
    for (auto& p : problems) std::cout << "  - skipped: " << p << "\n";
    return 0;
}

int cmdInspect(int argc, char** argv) {
    if (argc < 3) { std::cerr << "usage: niftool inspect <nif>\n"; return 2; }
    nifly::NifFile nif;
    if (nif.Load(argv[2]) != 0) { std::cerr << "{\"error\":\"cannot load NIF\"}\n"; return 1; }
    std::cout << inspectJson(nif).dump(2) << "\n";
    return 0;
}

int cmdVerify(int argc, char** argv) {
    if (argc < 3) { std::cerr << "usage: niftool verify <nif>\n"; return 2; }
    using namespace nifly;
    NifFile nif;
    int failures = 0;
    std::vector<std::string> checks;
    auto pass = [&](bool cond, const std::string& msg) {
        checks.push_back((cond ? "[ok]   " : "[FAIL] ") + msg);
        if (!cond) ++failures;
        return cond;
    };

    if (nif.Load(argv[2]) != 0) {
        std::cout << "RESULT: 1 checks, 1 failed\n[FAIL] load NIF: " << argv[2] << "\n";
        return 1;
    }
    pass(true, "load NIF");
    pass(nif.GetHeader().GetVersion().IsFO4(), "header is FO4 (20.2.0.7)");

    auto shapes = nif.GetShapes();
    pass(!shapes.empty(), "has at least one shape");
    for (auto* s : shapes) {
        const std::string n = s->name.get();
        pass(s->GetNumVertices() > 0, "'" + n + "' has vertices");
        pass(s->GetNumTriangles() > 0, "'" + n + "' has triangles");
        auto* shd = nif.GetShader(s);
        pass(shd != nullptr, "'" + n + "' has a shader property");

        if (!dynamic_cast<BSEffectShaderProperty*>(shd)) {
            std::string t;
            nif.GetTextureSlot(s, t, 0);
            pass(!t.empty(), "'" + n + "' has a diffuse texture (slot 0)");
        }
        std::vector<Vector3> tang;
        pass(nif.GetTangentsForShape(s, tang) && !tang.empty(), "'" + n + "' has tangents");
    }
    bool bsx = false;
    for (uint32_t i = 0; i < nif.GetHeader().GetNumBlocks(); ++i)
        if (nif.GetHeader().GetBlockTypeStringById(i) == "BSXFlags") bsx = true;
    pass(bsx, "has BSXFlags");

    std::cout << "RESULT: " << checks.size() << " checks, " << failures << " failed\n";
    for (auto& c : checks) std::cout << c << "\n";
    return failures == 0 ? 0 : 1;
}

int cmdGeomRot(int argc, char** argv) {
    using namespace nifly;
    if (argc < 7) { std::cerr << "RESULT: geomrot FAILED - usage: niftool geomrot <in.nif> <out.nif> <rx> <ry> <rz>  (degrees)\n"; return 2; }
    NifFile nif;
    if (nif.Load(argv[2]) != 0) { std::cerr << "RESULT: geomrot FAILED - cannot load\n"; return 1; }
    constexpr double kPi = 3.14159265358979323846;
    double ax = std::atof(argv[4]) * kPi / 180.0, ay = std::atof(argv[5]) * kPi / 180.0, az = std::atof(argv[6]) * kPi / 180.0;
    double cx = std::cos(ax), sx = std::sin(ax), cy = std::cos(ay), sy = std::sin(ay), cz = std::cos(az), sz = std::sin(az);
    float m00 = (float)(cz * cy), m01 = (float)(cz * sy * sx - sz * cx), m02 = (float)(cz * sy * cx + sz * sx);
    float m10 = (float)(sz * cy), m11 = (float)(sz * sy * sx + cz * cx), m12 = (float)(sz * sy * cx - cz * sx);
    float m20 = (float)(-sy),     m21 = (float)(cy * sx),                m22 = (float)(cy * cx);
    auto rotDir = [&](const Vector3& v) { return Vector3(m00 * v.x + m01 * v.y + m02 * v.z, m10 * v.x + m11 * v.y + m12 * v.z, m20 * v.x + m21 * v.y + m22 * v.z); };

    Vector3 mn(1e9f, 1e9f, 1e9f), mx(-1e9f, -1e9f, -1e9f);
    for (auto* s : nif.GetShapes()) {
        if (!s) continue;
        std::vector<Vector3> verts;
        if (!nif.GetVertsForShape(s, verts)) continue;
        for (auto& v : verts) {
            mn.x = std::min(mn.x, v.x); mn.y = std::min(mn.y, v.y); mn.z = std::min(mn.z, v.z);
            mx.x = std::max(mx.x, v.x); mx.y = std::max(mx.y, v.y); mx.z = std::max(mx.z, v.z);
        }
    }
    Vector3 c((mn.x + mx.x) * 0.5f, (mn.y + mx.y) * 0.5f, (mn.z + mx.z) * 0.5f);

    int shapes = 0;
    for (auto* s : nif.GetShapes()) {
        if (!s) continue;
        std::vector<Vector3> verts;
        if (nif.GetVertsForShape(s, verts)) {
            for (auto& v : verts) { Vector3 d(v.x - c.x, v.y - c.y, v.z - c.z); Vector3 r = rotDir(d); v = Vector3(r.x + c.x, r.y + c.y, r.z + c.z); }
            nif.SetVertsForShape(s, verts);
        }
        if (const auto* np = nif.GetNormalsForShape(s)) { std::vector<Vector3> n = *np; for (auto& v : n) v = rotDir(v); nif.SetNormalsForShape(s, n); }
        std::vector<Vector3> tang;
        if (nif.GetTangentsForShape(s, tang) && !tang.empty()) { for (auto& v : tang) v = rotDir(v); nif.SetTangentsForShape(s, tang); }
        std::vector<Vector3> bit;
        if (nif.GetBitangentsForShape(s, bit) && !bit.empty()) { for (auto& v : bit) v = rotDir(v); nif.SetBitangentsForShape(s, bit); }
        ++shapes;
    }
    if (nif.Save(argv[3]) != 0) { std::cerr << "RESULT: geomrot FAILED - cannot save\n"; return 1; }
    std::cout << "RESULT: geomrot OK -> " << argv[3] << " (" << shapes << " shapes, pivot " << c.x << "," << c.y << "," << c.z << ")\n";
    return 0;
}

int cmdGeomRotNames(int argc, char** argv) {
    using namespace nifly;
    if (argc < 8) {
        std::cerr << "RESULT: geomrotnames FAILED - usage: niftool geomrotnames <in.nif> <out.nif> <rx> <ry> <rz> <comma,separated,shape,names> [pivotX pivotY pivotZ]\n";
        return 2;
    }
    NifFile nif;
    if (nif.Load(argv[2]) != 0) { std::cerr << "RESULT: geomrotnames FAILED - cannot load\n"; return 1; }
    constexpr double kPi = 3.14159265358979323846;
    double ax = std::atof(argv[4]) * kPi / 180.0, ay = std::atof(argv[5]) * kPi / 180.0, az = std::atof(argv[6]) * kPi / 180.0;
    double cx = std::cos(ax), sx = std::sin(ax), cy = std::cos(ay), sy = std::sin(ay), cz = std::cos(az), sz = std::sin(az);
    float m00 = (float)(cz * cy), m01 = (float)(cz * sy * sx - sz * cx), m02 = (float)(cz * sy * cx + sz * sx);
    float m10 = (float)(sz * cy), m11 = (float)(sz * sy * sx + cz * cx), m12 = (float)(sz * sy * cx - cz * sx);
    float m20 = (float)(-sy),     m21 = (float)(cy * sx),                m22 = (float)(cy * cx);
    auto rotDir = [&](const Vector3& v) { return Vector3(m00 * v.x + m01 * v.y + m02 * v.z, m10 * v.x + m11 * v.y + m12 * v.z, m20 * v.x + m21 * v.y + m22 * v.z); };

    std::set<std::string> names;
    { std::stringstream ss(argv[7]); std::string tok; while (std::getline(ss, tok, ',')) names.insert(tok); }

    std::vector<NiShape*> targets;
    for (auto* s : nif.GetShapes()) if (s && names.count(s->name.get())) targets.push_back(s);
    if (targets.empty()) { std::cerr << "RESULT: geomrotnames FAILED - none of the given names matched a shape\n"; return 1; }

    Vector3 c;
    if (argc >= 11) {
        c = Vector3(static_cast<float>(std::atof(argv[8])), static_cast<float>(std::atof(argv[9])),
                    static_cast<float>(std::atof(argv[10])));
    } else {
        Vector3 mn(1e9f, 1e9f, 1e9f), mx(-1e9f, -1e9f, -1e9f);
        for (auto* s : targets) {
            std::vector<Vector3> verts;
            if (!nif.GetVertsForShape(s, verts)) continue;
            for (auto& v : verts) {
                mn.x = std::min(mn.x, v.x); mn.y = std::min(mn.y, v.y); mn.z = std::min(mn.z, v.z);
                mx.x = std::max(mx.x, v.x); mx.y = std::max(mx.y, v.y); mx.z = std::max(mx.z, v.z);
            }
        }
        c = Vector3((mn.x + mx.x) * 0.5f, (mn.y + mx.y) * 0.5f, (mn.z + mx.z) * 0.5f);
    }

    for (auto* s : targets) {
        std::vector<Vector3> verts;
        if (nif.GetVertsForShape(s, verts)) {
            for (auto& v : verts) { Vector3 d(v.x - c.x, v.y - c.y, v.z - c.z); Vector3 r = rotDir(d); v = Vector3(r.x + c.x, r.y + c.y, r.z + c.z); }
            nif.SetVertsForShape(s, verts);
        }
        if (const auto* np = nif.GetNormalsForShape(s)) { std::vector<Vector3> n = *np; for (auto& v : n) v = rotDir(v); nif.SetNormalsForShape(s, n); }
        std::vector<Vector3> tang;
        if (nif.GetTangentsForShape(s, tang) && !tang.empty()) { for (auto& v : tang) v = rotDir(v); nif.SetTangentsForShape(s, tang); }
        std::vector<Vector3> bit;
        if (nif.GetBitangentsForShape(s, bit) && !bit.empty()) { for (auto& v : bit) v = rotDir(v); nif.SetBitangentsForShape(s, bit); }
    }
    if (nif.Save(argv[3]) != 0) { std::cerr << "RESULT: geomrotnames FAILED - cannot save\n"; return 1; }
    std::cout << "RESULT: geomrotnames OK -> " << argv[3] << " (" << targets.size() << " shapes rotated, pivot "
               << c.x << "," << c.y << "," << c.z << ")\n";
    return 0;
}

int cmdGeoCenter(int argc, char** argv) {
    using namespace nifly;
    if (argc < 4) { std::cerr << "RESULT: geocenter FAILED - usage: niftool geocenter <in.nif> <out.nif>\n"; return 2; }
    NifFile nif;
    if (nif.Load(argv[2]) != 0) { std::cerr << "RESULT: geocenter FAILED - cannot load\n"; return 1; }
    Vector3 mn(1e9f, 1e9f, 1e9f), mx(-1e9f, -1e9f, -1e9f);
    for (auto* s : nif.GetShapes()) {
        if (!s) continue;
        std::vector<Vector3> verts;
        if (!nif.GetVertsForShape(s, verts)) continue;
        for (auto& v : verts) {
            mn.x = std::min(mn.x, v.x); mn.y = std::min(mn.y, v.y); mn.z = std::min(mn.z, v.z);
            mx.x = std::max(mx.x, v.x); mx.y = std::max(mx.y, v.y); mx.z = std::max(mx.z, v.z);
        }
    }
    Vector3 c((mn.x + mx.x) * 0.5f, (mn.y + mx.y) * 0.5f, (mn.z + mx.z) * 0.5f);
    int shapes = 0;
    for (auto* s : nif.GetShapes()) {
        if (!s) continue;
        std::vector<Vector3> verts;
        if (!nif.GetVertsForShape(s, verts)) continue;
        for (auto& v : verts) { v.x -= c.x; v.y -= c.y; v.z -= c.z; }
        nif.SetVertsForShape(s, verts);
        ++shapes;
    }
    if (nif.Save(argv[3]) != 0) { std::cerr << "RESULT: geocenter FAILED - cannot save\n"; return 1; }
    std::cout << "RESULT: geocenter OK -> " << argv[3] << " (" << shapes << " shapes shifted by " << -c.x << "," << -c.y << "," << -c.z << ")\n";
    return 0;
}

int cmdAddCollision(int argc, char** argv) {
    using namespace nifly;
    if (argc < 4) { std::cerr << "RESULT: addcollision FAILED - usage: niftool addcollision <in.nif> <out.nif> [mass=0.5] [layer=4]\n"; return 2; }
    NifFile nif;
    if (nif.Load(argv[2]) != 0) { std::cerr << "RESULT: addcollision FAILED - cannot load\n"; return 1; }
    auto* root = nif.GetRootNode();
    if (!root) { std::cerr << "RESULT: addcollision FAILED - no root\n"; return 1; }
    float mass = argc > 4 ? (float)std::atof(argv[4]) : 0.5f;
    uint8_t layer = argc > 5 ? (uint8_t)std::atoi(argv[5]) : 4;

    Vector3 mn(1e9f, 1e9f, 1e9f), mx(-1e9f, -1e9f, -1e9f);
    for (auto* s : nif.GetShapes()) {
        if (!s) continue;
        std::vector<Vector3> verts;
        if (!nif.GetVertsForShape(s, verts)) continue;
        for (auto& v : verts) {
            mn.x = std::min(mn.x, v.x); mn.y = std::min(mn.y, v.y); mn.z = std::min(mn.z, v.z);
            mx.x = std::max(mx.x, v.x); mx.y = std::max(mx.y, v.y); mx.z = std::max(mx.z, v.z);
        }
    }
    Vector3 center((mn.x + mx.x) * 0.5f, (mn.y + mx.y) * 0.5f, (mn.z + mx.z) * 0.5f);
    Vector3 he((mx.x - mn.x) * 0.5f, (mx.y - mn.y) * 0.5f, (mx.z - mn.z) * 0.5f);
    const float HK = 0.0142875f;

    auto box = std::make_unique<bhkBoxShape>();
    box->dimensions = Vector3(he.x * HK, he.y * HK, he.z * HK);
    box->radius = 0.0139f;
    box->SetMaterial(0);
    uint32_t boxId = nif.GetHeader().AddBlock(std::move(box));

    auto body = std::make_unique<bhkRigidBodyT>();
    body->shapeRef.index = boxId;
    body->collisionFilter.layer = layer;
    body->collisionFilterCopy = body->collisionFilter;
    Vector4 t; t.x = center.x * HK; t.y = center.y * HK; t.z = center.z * HK; t.w = 0.f;
    body->translation = t;
    body->center = t;
    QuaternionXYZW q; q.x = 0.f; q.y = 0.f; q.z = 0.f; q.w = 1.f;
    body->rotation = q;
    body->mass = mass;

    float hx = he.x * HK, hy = he.y * HK, hz = he.z * HK;
    body->inertiaMatrix[0] = mass / 3.f * (hy * hy + hz * hz);
    body->inertiaMatrix[5] = mass / 3.f * (hx * hx + hz * hz);
    body->inertiaMatrix[10] = mass / 3.f * (hx * hx + hy * hy);

    uint32_t bodyId = nif.GetHeader().AddBlock(std::move(body));

    auto collObj = std::make_unique<bhkCollisionObject>();
    collObj->flags = 1;
    collObj->bodyRef.index = bodyId;
    collObj->targetRef.index = nif.GetBlockID(root);
    uint32_t collId = nif.GetHeader().AddBlock(std::move(collObj));
    root->collisionRef.index = collId;

    if (nif.Save(argv[3]) != 0) { std::cerr << "RESULT: addcollision FAILED - cannot save\n"; return 1; }
    std::cout << "RESULT: addcollision OK -> " << argv[3] << " (mass " << mass << " layer " << (int)layer
              << " halfExtent " << he.x << "," << he.y << "," << he.z << " game units)\n";
    return 0;
}

int cmdBlocks(int argc, char** argv) {
    using namespace nifly;
    if (argc < 3) { std::cerr << "RESULT: blocks FAILED - usage: niftool blocks <in.nif>\n"; return 2; }
    NifFile nif;
    if (nif.Load(argv[2]) != 0) { std::cerr << "RESULT: blocks FAILED - cannot load\n"; return 1; }
    auto& hdr = nif.GetHeader();
    std::cout << "blocks (" << hdr.GetNumBlocks() << "):\n";
    for (uint32_t i = 0; i < hdr.GetNumBlocks(); ++i) {
        std::string t = hdr.GetBlockTypeStringById(i);
        std::cout << "  [" << i << "] " << t;
        if (t == "bhkPhysicsSystem") {
            if (auto* ps = hdr.GetBlock<bhkPhysicsSystem>(i)) std::cout << "  dataBytes=" << ps->data.size();
        } else if (t == "bhkNPCollisionObject") {
            if (auto* co = hdr.GetBlock<bhkNPCollisionObject>(i))
                std::cout << "  bodyID=" << co->bodyID << " bodyRef=" << co->bodyRef.index << " targetRef=" << co->targetRef.index << " flags=" << co->flags;
        }
        std::cout << "\n";
    }
    if (auto* root = nif.GetRootNode()) {
        std::cout << "root[" << nif.GetBlockID(root) << "] collisionRef=" << root->collisionRef.index << " extraData=[";
        for (auto& r : root->extraDataRefs) std::cout << r.index << " ";
        std::cout << "]\n";
    }
    std::cout << "RESULT: blocks OK\n";
    return 0;
}

int cmdGraftCollision(int argc, char** argv) {
    using namespace nifly;
    if (argc < 5) { std::cerr << "RESULT: graftcollision FAILED - usage: niftool graftcollision <target.nif> <out.nif> <source.nif>\n"; return 2; }
    NifFile tgt, src;
    if (tgt.Load(argv[2]) != 0) { std::cerr << "RESULT: graftcollision FAILED - cannot load target\n"; return 1; }
    if (src.Load(argv[4]) != 0) { std::cerr << "RESULT: graftcollision FAILED - cannot load source\n"; return 1; }
    auto* srcRoot = src.GetRootNode();
    auto* tgtRoot = tgt.GetRootNode();
    if (!srcRoot || !tgtRoot) { std::cerr << "RESULT: graftcollision FAILED - no root\n"; return 1; }

    NiObject* srcCO = src.GetHeader().GetBlock<NiObject>(srcRoot->collisionRef.index);
    if (!srcCO) { std::cerr << "RESULT: graftcollision FAILED - source has no collisionRef\n"; return 1; }

    bool foundPhys = false; uint32_t srcPhysId = 0;
    for (uint32_t i = 0; i < src.GetHeader().GetNumBlocks(); ++i)
        if (src.GetHeader().GetBlockTypeStringById(i) == "bhkPhysicsSystem") { srcPhysId = i; foundPhys = true; break; }
    if (!foundPhys) { std::cerr << "RESULT: graftcollision FAILED - source has no bhkPhysicsSystem\n"; return 1; }
    NiObject* srcPhys = src.GetHeader().GetBlock<NiObject>(srcPhysId);

    auto physClone = srcPhys->Clone();
    uint32_t physId = tgt.GetHeader().AddBlock(std::move(physClone));
    auto coClone = srcCO->Clone();
    uint32_t coId = tgt.GetHeader().AddBlock(std::move(coClone));

    tgtRoot->collisionRef.index = coId;
    if (auto* tgtCO = tgt.GetHeader().GetBlock<bhkNPCollisionObject>(coId)) {
        tgtCO->bodyRef.index = physId;
        tgtCO->targetRef.index = tgt.GetBlockID(tgtRoot);
    }

    if (tgt.Save(argv[3]) != 0) { std::cerr << "RESULT: graftcollision FAILED - cannot save\n"; return 1; }
    std::cout << "RESULT: graftcollision OK -> " << argv[3] << " (grafted bhkPhysicsSystem + "
              << src.GetHeader().GetBlockTypeStringById(srcRoot->collisionRef.index) << " from " << argv[4] << ")\n";
    return 0;
}

}

int main(int argc, char** argv) {
    if (argc < 2) {
        std::cerr << "niftool <import|importmulti|inspect|verify|geo|collision|tree|set|fix|geomrot|geomrotnames> ...\n";
        return 2;
    }
    std::string cmd = argv[1];
    try {
        if (cmd == "version") { std::cout << "{\"schemaVersion\":2}\n"; return 0; }
        if (cmd == "serve") {
            bool wantSkin = false, wantCollision = false;
            for (int i = 2; i < argc; ++i) {
                if (std::string(argv[i]) == "--skin") wantSkin = true;
                if (std::string(argv[i]) == "--collision") wantCollision = true;
            }
            std::string line;
            while (std::getline(std::cin, line)) {
                while (!line.empty() && (line.back() == '\r' || line.back() == '\n')) line.pop_back();
                if (line.empty()) continue;
                if (line == "__QUIT__") break;
                std::string prog = "niftool", sub = "geo", skin = "--skin", col = "--collision";
                std::vector<char*> av{ prog.data(), sub.data(), line.data() };
                if (wantSkin) av.push_back(skin.data());
                if (wantCollision) av.push_back(col.data());
                int rc = 1;
                try { rc = cmdGeo(static_cast<int>(av.size()), av.data()); }
                catch (const std::exception& e) { std::cout << "{\"error\":\"" << e.what() << "\"}"; }
                if (rc != 0) std::cout << "{\"error\":\"niftool geo failed\"}";
                std::cout << "\n__NIFTOOL_EOR__" << std::endl;
            }
            return 0;
        }
        if (cmd == "import") return cmdImport(argc, argv);
        if (cmd == "importmulti") return cmdImportMulti(argc, argv);
        if (cmd == "fix") return cmdFix(argc, argv);
        if (cmd == "addeffect") return cmdAddEffect(argc, argv);
        if (cmd == "addoverlay") return cmdAddOverlay(argc, argv);
        if (cmd == "inspect") return cmdInspect(argc, argv);
        if (cmd == "geo") return cmdGeo(argc, argv);
        if (cmd == "collision") return cmdCollision(argc, argv);
        if (cmd == "tree") return cmdTree(argc, argv);
        if (cmd == "set") return cmdSet(argc, argv);
        if (cmd == "geomrot") return cmdGeomRot(argc, argv);
        if (cmd == "geomrotnames") return cmdGeomRotNames(argc, argv);
        if (cmd == "geocenter") return cmdGeoCenter(argc, argv);
        if (cmd == "addcollision") return cmdAddCollision(argc, argv);
        if (cmd == "graftcollision") return cmdGraftCollision(argc, argv);
        if (cmd == "blocks") return cmdBlocks(argc, argv);
        if (cmd == "verify") return cmdVerify(argc, argv);
    } catch (const std::exception& e) {
        std::cerr << "RESULT: " << cmd << " FAILED - exception: " << e.what() << "\n";
        return 1;
    }
    std::cerr << "unknown command: " << cmd << "\n";
    return 2;
}
