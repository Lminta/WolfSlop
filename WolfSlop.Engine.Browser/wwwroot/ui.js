const identity = {a: 1, b: 0, c: 0, d: 1, e: 0, f: 0};
const sameMatrix = (a, b) => a.a === b.a && a.b === b.b && a.c === b.c && a.d === b.d && a.e === b.e && a.f === b.f;
const multiply = (a, b) => ({a: a.a*b.a+a.c*b.b, b: a.b*b.a+a.d*b.b,
    c: a.a*b.c+a.c*b.d, d: a.b*b.c+a.d*b.d, e: a.a*b.e+a.c*b.f+a.e, f: a.b*b.e+a.d*b.f+a.f});
export function mergeUiFrame(previous, update) {
    const nodes = new Map((previous?.nodes ?? []).map(node => [node.id, node]));
    for (const node of update.nodes) nodes.set(node.id,
        node.kind === 'canvas' && !node.drawing ? {...node, drawing: nodes.get(node.id)?.drawing ?? null} : node);
    for (const node of update.transforms ?? []) nodes.set(node.id, {...nodes.get(node.id), transform: node.transform});
    for (const node of update.texts ?? []) nodes.set(node.id, {...nodes.get(node.id), ...node});
    for (const node of update.canvases ?? []) {
        const previous = nodes.get(node.id);
        nodes.set(node.id, {...previous, drawingTransform: node.drawingTransform, drawing: node.replaceDrawing ? node.drawing : previous.drawing});
    }
    return {...update, nodes: update.order.map(id => nodes.get(id))};
}
export function inversePoint(matrix, x, y) {
    const determinant = matrix.a * matrix.d - matrix.b * matrix.c;
    if (Math.abs(determinant) < 1e-12) return null;
    x -= matrix.e; y -= matrix.f;
    return {x: (matrix.d * x - matrix.c * y) / determinant, y: (-matrix.b * x + matrix.a * y) / determinant};
}
const contains = (rect, point) => point && rect.width > 0 && rect.height > 0 &&
    point.x >= rect.x && point.y >= rect.y && point.x < rect.x + rect.width && point.y < rect.y + rect.height;
const containsClip = (clip, point) => {
    if (!contains(clip.rect, point)) return false;
    const radius = Math.max(0, Math.min(clip.radius || 0, clip.rect.width / 2, clip.rect.height / 2));
    const x = Math.max(clip.rect.x + radius, Math.min(clip.rect.x + clip.rect.width - radius, point.x));
    const y = Math.max(clip.rect.y + radius, Math.min(clip.rect.y + clip.rect.height - radius, point.y));
    return (point.x - x) ** 2 + (point.y - y) ** 2 <= radius ** 2;
};
export function hitTest(frame, x, y) {
    if (!frame) return null;
    const byId = new Map(frame.nodes.map(node => [node.id, node]));
    for (let i = frame.nodes.length - 1; i >= 0; i--) {
        const node = frame.nodes[i];
        if (!node.interactive || !node.enabled || node.opacity <= 0 ||
            !contains({x: 0, y: 0, width: node.width, height: node.height}, inversePoint(node.transform, x, y))) continue;
        if (node.clips.some(clip => !containsClip(clip, inversePoint(clip.transform, x, y)))) continue;
        if (frame.modalId) {
            let ancestor = node;
            while (ancestor && ancestor.id !== frame.modalId) ancestor = byId.get(ancestor.parentId);
            if (!ancestor) continue;
        }
        return node;
    }
    return frame.modalId ? byId.get(frame.modalId) : null;
}
const fontCss = font => `${font.italic ? 'italic ' : ''}${font.weight} ${font.size}px ${font.family}`;
function editorClip(node) {
    let polygon = [{x: 0, y: 0}, {x: node.width, y: 0}, {x: node.width, y: node.height}, {x: 0, y: node.height}];
    for (const clip of node.clips) {
        const r = clip.rect, t = clip.transform;
        const mask = [[r.x,r.y],[r.x+r.width,r.y],[r.x+r.width,r.y+r.height],[r.x,r.y+r.height]]
            .map(([x,y])=>inversePoint(node.transform,t.a*x+t.c*y+t.e,t.b*x+t.d*y+t.f));
        if (mask.some(point=>!point)) return 'polygon(0 0,0 0,0 0)';
        const orientation = Math.sign(mask.reduce((area,p,i)=>{const q=mask[(i+1)%4];return area+p.x*q.y-q.x*p.y;},0)) || 1;
        for (let i=0;i<mask.length && polygon.length;i++) {
            const a=mask[i],b=mask[(i+1)%4],input=polygon;polygon=[];
            const side=p=>orientation*((b.x-a.x)*(p.y-a.y)-(b.y-a.y)*(p.x-a.x));
            let previous=input.at(-1),before=side(previous);
            for(const point of input){const after=side(point);
                if((after>=0)!==(before>=0)){const amount=before/(before-after);polygon.push({x:previous.x+(point.x-previous.x)*amount,y:previous.y+(point.y-previous.y)*amount});}
                if(after>=0)polygon.push(point);previous=point;before=after;
            }
        }
    }
    return polygon.length ? 'polygon('+polygon.map(point=>`${point.x}px ${point.y}px`).join(',')+')' : 'polygon(0 0,0 0,0 0)';
}
const apply = (context, matrix) => context.transform(matrix.a, matrix.b, matrix.c, matrix.d, matrix.e, matrix.f);
function rectangle(context, x, y, width, height, radius = 0) {
    context.beginPath();
    if (radius > 0) context.roundRect(x, y, width, height, Math.min(radius, width / 2, height / 2));
    else context.rect(x, y, width, height);
}

export function drawShapes(context, drawing, transform = identity) {
    for (const shape of drawing.shapes) {
        context.save();
        const matrix = multiply(transform, shape.transform ?? identity);
        apply(context, matrix);
        const p = shape.points;
        context.beginPath();
        if (shape.kind === 'rect') context.rect(...p);
        else if (shape.kind === 'circle') context.arc(p[0], p[1], p[2], 0, Math.PI * 2);
        else if (p.length >= 2) {
            context.moveTo(p[0], p[1]);
            for (let i = 2; i < p.length; i += 2) context.lineTo(p[i], p[i + 1]);
            if (shape.kind === 'polygon') context.closePath();
        }
        if (shape.fill) {context.fillStyle = shape.fill; context.fill();}
        if (shape.stroke) {
            context.strokeStyle = shape.stroke;
            context.lineWidth = shape.lineWidth / (Math.sqrt(Math.abs(matrix.a * matrix.d - matrix.b * matrix.c)) || 1);
            context.lineJoin = 'round'; context.lineCap = 'round'; context.stroke();
        }
        context.restore();
    }
}

function drawText(context, node) {
    context.font = fontCss(node.font); context.fillStyle = node.color; context.textBaseline = 'top';
    const innerWidth = node.width - node.padding.left - node.padding.right;
    let y = node.kind === 'button' || node.kind === 'toggle' ? (node.height - node.textLayout.height) / 2 : node.padding.top;
    for (const line of node.textLayout.lines) {
        const x = node.padding.left + (node.textAlignment === 1 ? (innerWidth - line.width) / 2 : node.textAlignment === 2 ? innerWidth - line.width : 0);
        if (node.textOutlineColor && node.textOutlineWidth > 0) {
            context.strokeStyle = node.textOutlineColor; context.lineWidth = node.textOutlineWidth;
            context.lineJoin = 'round'; context.strokeText(line.text, x, y);
        }
        context.fillText(line.text, x, y); y += node.textLayout.lineHeight;
    }
}

export function drawNode(context, node, image, vectorSurface, textSurface) {
    if (node.opacity <= 0 || node.width <= 0 || node.height <= 0) return;
    context.save();
    for (const clip of node.clips) {
        context.save(); apply(context, clip.transform);
        rectangle(context, clip.rect.x, clip.rect.y, clip.rect.width, clip.rect.height, clip.radius);
        context.restore(); context.clip();
    }
    apply(context, node.transform);
    context.globalAlpha = node.opacity;
    rectangle(context, 0, 0, node.width, node.height, node.cornerRadius);
    if (node.background) { context.fillStyle = node.background; context.fill(); }
    if (node.borderColor && node.borderWidth > 0) {
        context.strokeStyle = node.borderColor; context.lineWidth = node.borderWidth; context.stroke();
    }
    if (node.kind === 'image') {
        if (image) {
            const uv = node.sourceRect, sourceWidth = image.naturalWidth * uv.width, sourceHeight = image.naturalHeight * uv.height;
            let x = 0, y = 0, width = node.width, height = node.height;
            if (node.imageFit !== 0) {
                const scale = node.imageFit === 1 ? Math.min(width / sourceWidth, height / sourceHeight) : Math.max(width / sourceWidth, height / sourceHeight);
                width = sourceWidth * scale; height = sourceHeight * scale; x = (node.width - width) / 2; y = (node.height - height) / 2;
                context.clip();
            }
            if (sourceWidth > 0 && sourceHeight > 0) context.drawImage(image, image.naturalWidth * uv.x, image.naturalHeight * uv.y, sourceWidth, sourceHeight, x, y, width, height);
        } else {context.fillStyle = '#ff00ff'; context.fillRect(0, 0, node.width, node.height);}
    } else if (node.kind === 'canvas' && vectorSurface) context.drawImage(vectorSurface, 0, 0, node.width, node.height);
    else if (node.kind === 'progress' || node.kind === 'slider') {
        context.fillStyle = node.background ?? '#23313d'; context.fillRect(0, 0, node.width, node.height);
        context.fillStyle = node.accent; context.fillRect(0, 0, node.width * node.value, node.height);
        if (node.kind === 'slider') {context.fillStyle = '#ffffff'; context.fillRect(Math.max(0, Math.min(node.width - 6, node.width * node.value - 3)), 0, 6, node.height);}
    }
    if (node.textLayout && node.font && !node.nativeEditing) {
        if (textSurface) context.drawImage(textSurface.canvas, -textSurface.margin, -textSurface.margin, textSurface.width, textSurface.height);
        else drawText(context, node);
    }
    context.restore();
}

export function createUi(world, callback) {
    const parent = world.parentElement, previousPosition = parent.style.position;
    const changedPosition = getComputedStyle(parent).position === 'static';
    if (changedPosition) parent.style.position = 'relative';
    const canvas = document.createElement('canvas'); canvas.className = 'wolf-ui-canvas';
    canvas.setAttribute('aria-hidden', 'true');
    Object.assign(canvas.style, {position: 'absolute', inset: '0', width: '100%', height: '100%', pointerEvents: 'none', zIndex: '2'});
    const semantics = document.createElement('div'); semantics.dataset.wolfUiAccessibility = '';
    Object.assign(semantics.style, {position: 'absolute', left: '0', top: '0', opacity: '0', pointerEvents: 'none', transformOrigin: '0 0', zIndex: '3'});
    const editors = document.createElement('div'); editors.dataset.wolfUiEditors = '';
    Object.assign(editors.style, {position: 'absolute', left: '0', top: '0', pointerEvents: 'none', transformOrigin: '0 0', zIndex: '3'});
    parent.append(canvas, semantics, editors);
    const context = canvas.getContext('2d', {alpha: true});
    const measuring = document.createElement('canvas').getContext('2d');
    const images = new Map(), vectors = new Map(), texts = new Map(), proxies = new Map(), subscriptions = [], fonts = new Set();
    let disposed = false, generation = 0, frame = null, drawCount = 0, lastUiClick = false, hover = null, syncingFocus = false;
    const pointers = new Set();
    Object.defineProperty(canvas, 'uiStats', {get: () => ({imageCount: images.size, vectorCount: vectors.size, draws: drawCount})});
    const send = event => !disposed && callback.invokeMethod('Dispatch', event);
    function listen(target, name, handler, options) {target.addEventListener(name, handler, options); subscriptions.push(() => target.removeEventListener(name, handler, options));}
    function getImage(source) {
        if (images.has(source)) return images.get(source);
        const image = new Image(), entry = {image, ready: false, failed: false, promise: null}, epoch = generation;
        image.src = source;
        entry.promise = image.decode().then(() => {entry.ready = true; return entry;}, () => {entry.failed = true; return entry;})
            .then(value => {if (!disposed && epoch === generation && frame) paint(); return value;});
        images.set(source, entry); return entry;
    }
    function vectorSurface(node) {
        if (!node.drawing || node.viewBox.width <= 0 || node.viewBox.height <= 0) return null;
        const width = Math.max(1, Math.ceil(node.width * canvas.width / frame.width)), height = Math.max(1, Math.ceil(node.height * canvas.height / frame.height));
        let cache = vectors.get(node.id);
        const transform = node.drawingTransform ?? identity, box = node.viewBox;
        if (cache?.revision === node.drawing.revision && cache.width === width && cache.height === height &&
            sameMatrix(cache.transform, transform) && cache.box.x === box.x && cache.box.y === box.y &&
            cache.box.width === box.width && cache.box.height === box.height) return cache.canvas;
        const surface = cache?.canvas ?? document.createElement('canvas');
        if (surface.width !== width || surface.height !== height) {surface.width = width; surface.height = height;}
        const ctx = surface.getContext('2d');
        ctx.setTransform(1, 0, 0, 1, 0, 0); ctx.clearRect(0, 0, width, height);
        ctx.scale(width / node.viewBox.width, height / node.viewBox.height); ctx.translate(-node.viewBox.x, -node.viewBox.y);
        drawShapes(ctx, node.drawing, transform);
        cache = {canvas: surface, revision: node.drawing.revision, drawing: node.drawing, width, height, transform, box}; vectors.set(node.id, cache); return surface;
    }
    function textSurface(node) {
        if (!node.textLayout || !node.font || node.nativeEditing || node.width <= 0 || node.height <= 0) return null;
        const scaleX = canvas.width / frame.width, scaleY = canvas.height / frame.height;
        let cache = texts.get(node.id);
        const old = cache?.node;
        if (old && cache.scaleX === scaleX && cache.scaleY === scaleY && old.textLayout === node.textLayout &&
            old.font === node.font && old.color === node.color && old.padding === node.padding &&
            old.width === node.width && old.height === node.height && old.kind === node.kind &&
            old.textAlignment === node.textAlignment && old.textOutlineColor === node.textOutlineColor &&
            old.textOutlineWidth === node.textOutlineWidth) return cache;
        // Leave room for italic glyph overhang and outlines outside the layout box.
        const margin = Math.ceil(node.font.size + (node.textOutlineWidth || 0));
        const width = node.width + margin * 2, height = node.height + margin * 2;
        const surface = cache?.canvas ?? document.createElement('canvas');
        surface.width = Math.max(1, Math.ceil(width * scaleX)); surface.height = Math.max(1, Math.ceil(height * scaleY));
        const ctx = surface.getContext('2d');
        ctx.scale(surface.width / width, surface.height / height); ctx.translate(margin, margin);
        drawText(ctx, node);
        cache = {canvas: surface, node, margin, width, height, scaleX, scaleY}; texts.set(node.id, cache); return cache;
    }
    function paint() {
        if (disposed || !frame) return;
        const rect = world.getBoundingClientRect(), ratio = window.devicePixelRatio || 1;
        Object.assign(canvas.style, {left: world.offsetLeft + 'px', top: world.offsetTop + 'px', width: rect.width + 'px', height: rect.height + 'px', right: 'auto', bottom: 'auto'});
        Object.assign(semantics.style, {left: world.offsetLeft + 'px', top: world.offsetTop + 'px'});
        Object.assign(editors.style, {left: world.offsetLeft + 'px', top: world.offsetTop + 'px'});
        const width = Math.max(1, Math.min(4096, Math.round(rect.width * ratio))), height = Math.max(1, Math.min(4096, Math.round(rect.height * ratio)));
        if (canvas.width !== width || canvas.height !== height) {canvas.width = width; canvas.height = height;}
        context.setTransform(1, 0, 0, 1, 0, 0); context.clearRect(0, 0, width, height);
        context.scale(width / frame.width, height / frame.height);
        for (const node of frame.nodes) {
            const entry = node.source ? getImage(node.source) : null;
            drawNode(context, node, entry?.ready ? entry.image : null, node.kind === 'canvas' ? vectorSurface(node) : null, textSurface(node));
        }
        Object.assign(semantics.style, {width: frame.width + 'px', height: frame.height + 'px', transform: `scale(${rect.width / frame.width},${rect.height / frame.height})`});
        Object.assign(editors.style, {width: frame.width + 'px', height: frame.height + 'px', transform: `scale(${rect.width / frame.width},${rect.height / frame.height})`});
        drawCount++;
    }
    const htmlTag = role => ({button: 'button', table: 'table', rowgroup: 'tbody', row: 'tr', cell: 'td', columnheader: 'th', rowheader: 'th', heading: 'h2'}[role] ?? 'div');
    function syncSemantics() {
        const nodes = new Map(frame.nodes.map(node => [node.id, node])), needed = new Set();
        const ownedEditors = new Map();
        const attribute = (element, name, value) => {
            if (value == null) {if (element.hasAttribute(name)) element.removeAttribute(name);}
            else if (element.getAttribute(name) !== String(value)) element.setAttribute(name, String(value));
        };
        for (const node of frame.nodes) if (node.accessible) {
            let current = node;
            while (current && !needed.has(current.id)) {needed.add(current.id); current = nodes.get(current.parentId);}
        }
        for (const [id, element] of proxies) if (!needed.has(id)) {element.remove(); proxies.delete(id);}
        for (const node of frame.nodes) if (needed.has(node.id)) {
            let element = proxies.get(node.id);
            if (!element) {
                element = document.createElement(node.kind === 'input' ? 'input' : htmlTag(node.role)); element.dataset.uiId = node.id; element.id = 'wolf-ui-' + node.id;
                if (node.role === 'button') element.type = 'button';
                if (node.kind !== 'input') element.addEventListener('click', event => {event.preventDefault(); event.stopPropagation(); send({kind: 'activate', id: node.id});});
                element.addEventListener('focus', () => {if (!syncingFocus) send({kind: 'focus', id: node.id});});
                if (node.kind === 'input') element.addEventListener('input', () => send({kind: 'text', id: node.id, value: element.value}));
                proxies.set(node.id, element);
            }
            const target = node.kind === 'input' ? editors : proxies.get(node.parentId) ?? semantics;
            if (element.parentElement !== target) target.append(element);
            if (node.kind === 'input') {
                const owner = proxies.get(node.parentId) ?? semantics;
                if (!ownedEditors.has(owner)) ownedEditors.set(owner, []);
                ownedEditors.get(owner).push(element.id);
            }
            const parentNode = nodes.get(node.parentId), parentTransform = parentNode?.transform ?? identity;
            // Restore local coordinates from the composed transform.
            const det = parentTransform.a * parentTransform.d - parentTransform.b * parentTransform.c;
            const t = node.transform, m = node.kind === 'input' ? t : Math.abs(det) > 1e-12 ? {
                a: (parentTransform.d * t.a - parentTransform.c * t.b) / det,
                b: (-parentTransform.b * t.a + parentTransform.a * t.b) / det,
                c: (parentTransform.d * t.c - parentTransform.c * t.d) / det,
                d: (-parentTransform.b * t.c + parentTransform.a * t.d) / det,
                ...(() => {const p = inversePoint(parentTransform, t.e, t.f); return {e: p.x, f: p.y};})()
            } : t;
            let style = `all:initial;display:block;position:absolute;left:0;top:0;width:${node.width}px;height:${node.height}px;transform-origin:0 0;transform:matrix(${m.a},${m.b},${m.c},${m.d},${m.e},${m.f});pointer-events:none;`;
            if (node.kind === 'input') {
                style += `box-sizing:border-box;opacity:${node.nativeEditing ? node.opacity : 0};background:transparent;color:${node.color};caret-color:${node.accent};font:${fontCss(node.font)};padding:${node.padding.top}px ${node.padding.right}px ${node.padding.bottom}px ${node.padding.left}px;pointer-events:${node.nativeEditing ? 'auto' : 'none'};clip-path:${editorClip(node)};`;
                element.type = node.password ? 'password' : 'text'; element.maxLength = node.maxLength; element.readOnly = node.readOnly;
                element.placeholder = node.placeholder ?? '';
                if (element.value !== node.inputValue) element.value = node.inputValue ?? '';
            }
            if (element.__uiStyle !== style) {element.style.cssText = style; element.__uiStyle = style;}
            if (element.className !== (node.className ?? '')) element.className = node.className ?? '';
            if (node.kind !== 'input' && node.content != null && element.textContent !== node.content) element.textContent = node.content;
            attribute(element, 'role', node.role);
            attribute(element, 'aria-label', node.label);
            attribute(element, 'aria-disabled', !node.enabled);
            attribute(element, 'aria-pressed', node.checked);
            attribute(element, 'aria-valuenow', node.value);
            if (node.value != null) {attribute(element, 'aria-valuemin', 0); attribute(element, 'aria-valuemax', 1);}
            if (node.role === 'dialog') attribute(element, 'aria-modal', frame.modalId === node.id);
            const tabIndex = node.focusable && node.enabled ? node.tabIndex : -1;
            if (element.tabIndex !== tabIndex) element.tabIndex = tabIndex;
            if ('disabled' in element && element.disabled !== !node.enabled) element.disabled = !node.enabled;
        }
        for (const element of [semantics, ...proxies.values()]) attribute(element, 'aria-owns', ownedEditors.get(element)?.join(' ') ?? null);
        const focused = proxies.get(frame.focusedId);
        if (focused && document.activeElement !== focused) {
            syncingFocus = true; focused.focus({preventScroll: true}); syncingFocus = false;
        }
    }
    function point(event) {const rect = world.getBoundingClientRect(); return {x: (event.clientX - rect.left) * frame.width / rect.width, y: (event.clientY - rect.top) * frame.height / rect.height};}
    listen(world, 'pointerdown', event => {
        if (!frame || document.pointerLockElement || event.button !== 0) return;
        const p = point(event), hit = hitTest(frame, p.x, p.y); lastUiClick = !!hit;
        if (!hit) return;
        event.preventDefault(); event.stopImmediatePropagation(); pointers.add(event.pointerId);
        world.setPointerCapture?.(event.pointerId); send({kind: 'down', ...p, pointerId: event.pointerId});
    }, true);
    listen(world, 'pointermove', event => {
        if (!frame || document.pointerLockElement) return;
        const p = point(event), hit = hitTest(frame, p.x, p.y);
        if (!pointers.has(event.pointerId) && !hit && !hover) return;
        hover = hit?.id ?? null; send({kind: 'move', ...p, pointerId: event.pointerId});
    }, true);
    listen(world, 'pointerup', event => {
        if (!frame || !pointers.delete(event.pointerId)) return;
        event.preventDefault(); event.stopImmediatePropagation();
        send({kind: 'up', ...point(event), pointerId: event.pointerId}); world.releasePointerCapture?.(event.pointerId);
    }, true);
    listen(world, 'pointercancel', () => {pointers.clear(); send({kind: 'cancel'});}, true);
    listen(world, 'pointerleave', () => {if (!pointers.size) {hover = null; send({kind: 'cancel'});}}, true);
    listen(world, 'lostpointercapture', event => {if (pointers.delete(event.pointerId)) send({kind: 'cancel'});}, true);
    listen(world, 'wheel', event => {
        if (!frame || document.pointerLockElement) return;
        const p = point(event);
        if (send({kind: 'wheel', ...p, delta: event.deltaY * (event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? frame.height : 1)})) {
            event.preventDefault(); event.stopImmediatePropagation();
        }
    }, {capture: true, passive: false});
    listen(world, 'click', event => {
        if (!frame || document.pointerLockElement) return;
        const p = point(event);
        if (lastUiClick || hitTest(frame, p.x, p.y)) {event.preventDefault(); event.stopImmediatePropagation(); lastUiClick = false;}
    }, true);
    listen(window, 'keydown', event => {
        if (!frame || document.pointerLockElement || !(semantics.contains(document.activeElement) || editors.contains(document.activeElement) || document.activeElement === world)) return;
        if (send({kind: 'key', key: event.key, shift: event.shiftKey, repeat: event.repeat})) {event.preventDefault(); event.stopImmediatePropagation();}
    }, true);
    listen(window, 'blur', () => {pointers.clear(); hover = null; send({kind: 'cancel'});});
    const observer = new ResizeObserver(() => {
        const rect = world.getBoundingClientRect();
        send({kind: 'resize', x: Math.max(1, rect.width), y: Math.max(1, rect.height)});
        paint();
    }); observer.observe(world);
    function reset() {
        generation++; frame = null; canvas.__uiFrame = null;
        for (const pointer of pointers) if (world.hasPointerCapture?.(pointer)) world.releasePointerCapture(pointer);
        for (const entry of images.values()) entry.image.removeAttribute('src');
        images.clear(); vectors.clear(); texts.clear(); proxies.clear(); semantics.replaceChildren(); editors.replaceChildren(); pointers.clear(); hover = null; lastUiClick = false;
        context.setTransform(1, 0, 0, 1, 0, 0); context.clearRect(0, 0, canvas.width, canvas.height);
    }
    return {
        size() {const rect = world.getBoundingClientRect(); return [Math.max(1, rect.width), Math.max(1, rect.height)];},
        measure(text, font) {measuring.font = fontCss(font); return measuring.measureText(text).width;},
        async loadFont(family, source) {
            const face = new FontFace(family, `url(${JSON.stringify(new URL(source, document.baseURI).href)})`);
            await face.load();
            if (disposed) return;
            document.fonts.add(face); fonts.add(face);
        },
        async prepare(sources) {return Promise.all(sources.map(async source => {const entry = await getImage(source).promise; return {source, width: entry.ready ? entry.image.naturalWidth : 0, height: entry.ready ? entry.image.naturalHeight : 0};}));},
        renderDelta(value) {
            const semanticsChanged = !frame || value.nodes.length > 0 || value.texts?.length > 0 ||
                value.transforms?.some(node => proxies.has(node.id)) || frame.focusedId !== value.focusedId ||
                frame.modalId !== value.modalId || frame.nodes.length !== value.order.length ||
                frame.nodes.some((node, index) => node.id !== value.order[index]);
            this.render(mergeUiFrame(frame, value), semanticsChanged);
        },
        renderTransforms(version, transforms) {
            if (disposed || !frame) return;
            const changed = new Map(transforms.map(node => [node.id, node.transform]));
            frame = {...frame, version, nodes: frame.nodes.map(node => changed.has(node.id) ? {...node, transform: changed.get(node.id)} : node)};
            canvas.__uiFrame = frame;
            // Decorative transforms cannot affect the accessibility tree. A
            // transformed accessible node (or its ancestor) still updates it.
            if (transforms.some(node => proxies.has(node.id))) syncSemantics();
            paint();
        },
        render(value, semanticsChanged = true) {
            if (disposed) return;
            for (const node of value.nodes) if (node.kind === 'canvas' && !node.drawing) vectors.delete(node.id);
            for (const node of value.nodes) node.nativeEditing = node.kind === 'input' && value.focusedId === node.id && node.enabled;
            frame = value; canvas.__uiFrame = frame;
            const present = new Set(frame.nodes.map(node => node.id));
            for (const id of vectors.keys()) if (!present.has(id)) vectors.delete(id);
            for (const id of texts.keys()) if (!present.has(id)) texts.delete(id);
            if (semanticsChanged) syncSemantics();
            paint();
        },
        reset,
        dispose() {
            if (disposed) return; reset(); disposed = true;
            observer.disconnect(); subscriptions.forEach(remove => remove()); canvas.remove(); semantics.remove(); editors.remove();
            for (const face of fonts) document.fonts.delete(face); fonts.clear();
            if (changedPosition) parent.style.position = previousPosition;
        }
    };
}
