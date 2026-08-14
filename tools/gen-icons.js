// DshNotifyicon 图标生成（基于 DeepSeek 官方 favicon.svg，经 sharp 渲染）
// 样式：黑色鲸鱼（官方原色）、透明背景、无边框；运行态右下角绿点。
// 产物：DshNotifyicon/Assets/app.ico、app-running.ico、tools/preview-*.png
'use strict';
const sharp = require('C:/Users/dev/.dsh/profiles/node_modules/sharp');
const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '..');
const assets = path.join(root, 'DshNotifyicon', 'Assets'); // 项目目录内的资源
const tools = __dirname;
fs.mkdirSync(assets, { recursive: true });

const CANVAS = 512;
const GREEN = '#22C55E';

function dotSvg() {
  // 右下角状态点：直径 ≈ 画布 23%（16px 托盘下 ≈3.6px），可辨识的徽标比例
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${CANVAS}" height="${CANVAS}" viewBox="0 0 ${CANVAS} ${CANVAS}">
  <circle cx="450" cy="450" r="58" fill="${GREEN}"/></svg>`;
}

function buildIco(pngs) {
  const count = pngs.length;
  const header = Buffer.alloc(6); // ICONDIR：reserved + type + count
  header.writeUInt16LE(0, 0);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(count, 4);
  let offset = 6 + 16 * count; // 数据区起点 = ICONDIR + 全部条目表
  const entries = [];
  for (const p of pngs) {
    const e = Buffer.alloc(16);
    e[0] = p.size >= 256 ? 0 : p.size;
    e[1] = p.size >= 256 ? 0 : p.size;
    e.writeUInt16LE(1, 4); // planes
    e.writeUInt16LE(32, 6); // bpp
    e.writeUInt32LE(p.data.length, 8);
    e.writeUInt32LE(offset, 12);
    entries.push(e);
    offset += p.data.length;
  }
  return Buffer.concat([header, ...entries, ...pngs.map((p) => p.data)]);
}

async function renderMaster(withDot) {
  // 黑色鲸鱼（官方 favicon 原色，不反转、不加背景），放大占满画布 ~95%，合成到透明画布
  const logo = await sharp(path.join(tools, 'favicon.svg'))
    .resize(486, 486)
    .png()
    .toBuffer();
  const inputs = [{ input: logo, gravity: 'center' }];
  if (withDot) inputs.push({ input: Buffer.from(dotSvg()) });
  return sharp({
    create: { width: CANVAS, height: CANVAS, channels: 4, background: { r: 0, g: 0, b: 0, alpha: 0 } }
  })
    .composite(inputs)
    .png()
    .toBuffer();
}

async function gen(withDot, outName, previewName) {
  const master = await renderMaster(withDot);
  const sizes = [16, 24, 32, 48, 256];
  const pngs = [];
  for (const s of sizes) {
    const data = await sharp(master).resize(s, s).png().toBuffer();
    pngs.push({ size: s, data });
    if (s === 32) fs.writeFileSync(path.join(tools, previewName), data);
  }
  const ico = buildIco(pngs);
  fs.writeFileSync(path.join(assets, outName), ico);
  console.log(`written: ${path.join(assets, outName)} (${ico.length} bytes)`);
}

(async () => {
  await gen(false, 'app.ico', 'preview-app.png');
  await gen(true, 'app-running.ico', 'preview-app-running.png');
})().catch((e) => { console.error(e); process.exit(1); });
