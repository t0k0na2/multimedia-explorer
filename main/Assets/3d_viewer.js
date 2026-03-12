import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';
import { FBXLoader } from 'three/examples/jsm/loaders/FBXLoader.js';
import { OBJLoader } from 'three/examples/jsm/loaders/OBJLoader.js';

let camera, scene, renderer, controls, currentModel;
const loadingElement = document.getElementById('loading');

init();
animate();

function init() {
    // Scene Setup
    scene = new THREE.Scene();
    // Optional: Background color
    scene.background = null; // transparent to show overlay effect if possible

    // Camera Setup
    camera = new THREE.PerspectiveCamera(45, window.innerWidth / window.innerHeight, 0.1, 1000);
    camera.position.set(0, 2, 5);

    // Renderer Setup
    renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
    renderer.setPixelRatio(window.devicePixelRatio);
    renderer.setSize(window.innerWidth, window.innerHeight);
    // tone map and encoding for better gltf look
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    renderer.toneMappingExposure = 1.0;
    document.body.appendChild(renderer.domElement);

    // Controls
    controls = new OrbitControls(camera, renderer.domElement);
    controls.enableDamping = true;
    controls.dampingFactor = 0.05;
    controls.screenSpacePanning = true;

    // Lights
    const ambientLight = new THREE.AmbientLight(0xffffff, 0.6);
    scene.add(ambientLight);

    const directionalLight = new THREE.DirectionalLight(0xffffff, 1.5);
    directionalLight.position.set(5, 10, 7.5);
    scene.add(directionalLight);

    // Window Resize Handle
    window.addEventListener('resize', onWindowResize);

    // Communicate with C# Host
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', event => {
            let msg = event.data;
            if (typeof msg === 'string') {
                try { msg = JSON.parse(msg); } catch (e) { }
            }
            console.log(msg);
            if (msg.action === 'load') {
                console.log("load action: " + msg.action);
                loadModel(msg.url, msg.extension, false);
            } else if (msg.action === 'thumbnail') {
                console.log("thumbnail action: " + msg.action);
                loadModel(msg.url, msg.extension, true);
            } else if (msg.action === 'clear') {
                console.log("clear action: " + msg.action);
                clearModel();
            }
            else {
                console.log("Unknown action: " + msg.action);
            }
        });
    }
}

function onWindowResize() {
    camera.aspect = window.innerWidth / window.innerHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(window.innerWidth, window.innerHeight);
}

function animate() {
    requestAnimationFrame(animate);
    controls.update();
    renderer.render(scene, camera);
}

function clearModel() {
    if (currentModel) {
        scene.remove(currentModel);
        // traverse and dispose geometries/materials
        currentModel.traverse(child => {
            if (child.isMesh) {
                child.geometry.dispose();
                if (child.material.isMaterial) {
                    cleanMaterial(child.material);
                } else if (Array.isArray(child.material)) {
                    child.material.forEach(cleanMaterial);
                }
            }
        });
        currentModel = null;
    }
    if (loadingElement) loadingElement.style.display = 'none';
    // reset camera
    camera.position.set(0, 2, 5);
    controls.target.set(0, 0, 0);
    controls.update();
}

function cleanMaterial(material) {
    material.dispose();
    if (material.map) material.map.dispose();
    if (material.lightMap) material.lightMap.dispose();
    if (material.bumpMap) material.bumpMap.dispose();
    if (material.normalMap) material.normalMap.dispose();
    if (material.specularMap) material.specularMap.dispose();
    if (material.envMap) material.envMap.dispose();
}

function loadModel(url, extension, isThumbnail) {
    console.log("Loading model: " + url + ", isThumbnail: " + isThumbnail);
    clearModel();
    if (!isThumbnail && loadingElement) {
        loadingElement.style.display = 'block';
    }

    let loader;
    const ext = extension.toLowerCase();

    if (ext === '.gltf' || ext === '.glb') {
        loader = new GLTFLoader();
    } else if (ext === '.fbx') {
        loader = new FBXLoader();
    } else if (ext === '.obj') {
        loader = new OBJLoader();
    } else {
        console.error("Unsupported format: " + ext);
        if (loadingElement) loadingElement.innerText = "未対応のプレビューフォーマットです";
        return;
    }

    console.log("Loading model: " + url);
    loader.load(
        url, // model url
        (object) => { // onLoad
            if (!isThumbnail && loadingElement) {
                loadingElement.style.display = 'none';
                loadingElement.innerText = "読み込み中...";
            }

            // Center and scale the model
            let root = (ext === '.gltf' || ext === '.glb') ? object.scene : object;

            const box = new THREE.Box3().setFromObject(root);
            const center = box.getCenter(new THREE.Vector3());
            const size = box.getSize(new THREE.Vector3());

            // Offset the root to center its bounding box at the origin
            root.position.x -= center.x;
            root.position.y -= center.y;
            root.position.z -= center.z;

            // Use a wrapper group to scale the centered model
            const wrapper = new THREE.Group();
            wrapper.add(root);

            // auto scale
            const maxDim = Math.max(size.x, size.y, size.z);
            const defaultSize = 3;
            if (maxDim > 0) {
                const scale = defaultSize / maxDim;
                wrapper.scale.set(scale, scale, scale);
            }

            scene.add(wrapper);
            currentModel = wrapper;

            // reset camera near object
            controls.target.set(0, 0, 0);
            camera.position.set(0, defaultSize * 0.3, defaultSize * 1.5);
            controls.update();

            if (isThumbnail) {
                // thumbnail logic
                renderer.render(scene, camera); // force render one frame
                setTimeout(() => {
                   const dataUrl = renderer.domElement.toDataURL('image/png');
                   window.chrome.webview.postMessage({ action: 'thumbnail_result', data: dataUrl, url: url });
                }, 100);
            }
        },
        (xhr) => { // onProgress
            const percent = Math.floor((xhr.loaded / xhr.total) * 100);
            if (loadingElement) {
                if (Number.isFinite(percent)) {
                    loadingElement.innerText = `読み込み中... ${percent}%`;
                } else {
                    loadingElement.innerText = `読み込み中...`;
                }
            }
        },
        (error) => { // onError
            console.error('An error happened', error);
            if (loadingElement) {
                loadingElement.style.display = 'block';
                loadingElement.innerText = "読み込みエラー";
            }
        }
    );
}
