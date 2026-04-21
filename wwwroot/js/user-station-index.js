(function () {
    const config = window.stationPageConfig || {};
    const MAP4D_API_KEY = config.apiKey || window.MAP4D_API_KEY || '';
    const isAuthenticated = String(config.isAuthenticated) === 'true' || config.isAuthenticated === true;

    let stationMap = null;
    let stationPager = null;
    let allStations = [];
    let currentStations = [];
    let currentMarkers = [];
    let userMarker = null;
    let userPosition = null;
    let selectedStationId = null;
    let keywordSuggestItems = [];
    let keywordSuggestIndex = -1;
    let keywordSuggestTimer = null;
    let keywordSuggestAbortController = null;
    let hasInitialFit = false;
    let infoPanel = null;
    let lastNearMeRadiusKm = null;

    document.addEventListener('DOMContentLoaded', async function () {
        try {
            await initMap();
            initPager();
            bindEvents();
            ensureInfoPanel();
            await loadStations({ fitAll: true });
        } catch (error) {
            console.error(error);
            setErrorState('Không khởi tạo được bản đồ trạm sạc.');
        }
    });

    async function initMap() {
        await TramSacMap4D.ensureSdk(MAP4D_API_KEY);

        const mapElement = document.getElementById('stationMap');
        if (!mapElement) {
            throw new Error('Không tìm thấy #stationMap');
        }

        stationMap = new map4d.Map(mapElement, {
            center: { lat: 16.5, lng: 107.5 },
            zoom: 6,
            controls: true
        });

        await wait(180);
    }

    function initPager() {
        stationPager = TramSacPagination.createPager({
            pageSize: 6,
            paginationContainer: 'stationPager',
            infoContainer: 'stationPageInfo',
            onPageChange: renderStationsPage
        });
    }

    function bindEvents() {
        document.getElementById('btnApplyFilter')?.addEventListener('click', function () {
            closeKeywordSuggest();
            applyFilters();
        });

        document.getElementById('btnLoadAll')?.addEventListener('click', function () {
            closeKeywordSuggest();
            applyLoadAll();
        });

        document.getElementById('btnUseLocation')?.addEventListener('click', async function () {
            closeKeywordSuggest();
            await zoomNearMe();
        });

        document.getElementById('btnResetFilter')?.addEventListener('click', async function () {
            resetFilters();
            await applyLoadAll();
        });

        const keywordInput = document.getElementById('keyword');
        const locationInput = document.getElementById('location');

        keywordInput?.addEventListener('input', function () {
            queueKeywordSuggest();
        });

        keywordInput?.addEventListener('keydown', function (e) {
            if (handleKeywordSuggestKeydown(e)) {
                return;
            }

            if (e.key === 'Enter') {
                e.preventDefault();
                closeKeywordSuggest();
                applyFilters();
            }
        });

        keywordInput?.addEventListener('focus', function () {
            if ((keywordInput.value || '').trim().length >= 2) {
                queueKeywordSuggest(true);
            }
        });

        locationInput?.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                applyFilters();
            }
        });

        document.addEventListener('click', function (e) {
            if (!e.target.closest('.station-smart-wrap')) {
                closeKeywordSuggest();
            }
        });
    }

    function getFilterValues() {
        return {
            keyword: document.getElementById('keyword')?.value.trim() || '',
            status: document.getElementById('status')?.value || '',
            location: document.getElementById('location')?.value.trim() || '',
            sortBy: document.getElementById('sortBy')?.value || 'smart',
            radiusKm: Number(document.getElementById('radiusKm')?.value || 3)
        };
    }

    async function loadStations(options = {}) {
        try {
            setLoadingState('Đang tải dữ liệu trạm sạc...');

            const response = await fetch('/User/api/stations');
            if (!response.ok) {
                throw new Error('Không thể tải dữ liệu trạm.');
            }

            const data = await response.json();
            allStations = Array.isArray(data) ? data : [];
            await applyFilters(options);
        } catch (error) {
            console.error(error);
            setErrorState('Không thể tải dữ liệu trạm sạc.');
        }
    }

    async function applyFilters(options = {}) {
        const filters = getFilterValues();
        currentStations = applySorting(filterStations(allStations, filters), filters.sortBy);
        updateKpis(currentStations);
        updateResultUI(currentStations.length, buildMapHint(filters));
        stationPager.setItems(currentStations);
        renderMarkers(currentStations, options.fitAll === true || (!hasInitialFit && currentStations.length > 0));

        if (options.focusStationId) {
            focusStation(Number(options.focusStationId), true);
        }
    }

    async function applyLoadAll() {
        currentStations = applySorting(allStations.slice(), 'smart');
        selectedStationId = null;
        updateKpis(currentStations);
        updateResultUI(currentStations.length, 'Đang hiển thị toàn bộ trạm trong hệ thống.');
        stationPager.setItems(currentStations);
        renderMarkers(currentStations, true);
        hideInfoPanel();
    }

    async function zoomNearMe() {
        try {
            if (!navigator.geolocation) {
                alert('Trình duyệt không hỗ trợ định vị.');
                return;
            }

            const position = await getCurrentPosition();
            const lat = position.coords.latitude;
            const lng = position.coords.longitude;
            const radiusKm = getFilterValues().radiusKm;

            userPosition = { lat, lng };
            lastNearMeRadiusKm = radiusKm;
            renderUserMarker(lat, lng);
            focusLocationByRadius(userPosition, radiusKm);

            const nearbyStations = (currentStations || []).filter(station => calcDistanceKm(userPosition, station) <= radiusKm);
            const points = [userPosition].concat(nearbyStations.map(station => ({ lat: Number(station.latitude), lng: Number(station.longitude) })));

            if (points.length > 1) {
                TramSacMap4D.fitBounds(stationMap, points, userPosition, getRadiusZoom(radiusKm));
            } else {
                focusMap(userPosition, getRadiusZoom(radiusKm));
            }

            const note = document.getElementById('locationNote');
            if (note) {
                if (nearbyStations.length > 0) {
                    note.innerHTML = `Đã xác định vị trí của bạn. Bản đồ đang zoom quanh <strong>bạn + ${nearbyStations.length} trạm</strong> trong phạm vi khoảng <strong>${radiusKm} km</strong>. Lăn chuột zoom ra vẫn xem được toàn bộ trạm.`;
                } else {
                    note.innerHTML = `Đã xác định vị trí của bạn nhưng chưa có trạm nào trong phạm vi khoảng <strong>${radiusKm} km</strong>. Bản đồ đang zoom về vị trí hiện tại của bạn.`;
                }
            }

            updateResultUI(currentStations.length, `Đang hiển thị toàn bộ trạm. Bản đồ đang zoom quanh vị trí của bạn trong bán kính khoảng ${radiusKm} km.`);
        } catch (error) {
            console.error(error);
            alert('Không lấy được vị trí hiện tại. Hãy kiểm tra quyền định vị của trình duyệt.');
        }
    }

    function filterStations(items, filters) {
        const keyword = normalize(filters.keyword);
        const location = normalize(filters.location);
        const status = normalize(filters.status);

        return (items || []).filter(station => {
            if (status && normalize(station.status) !== status) {
                return false;
            }

            if (keyword) {
                const haystack = [station.name, station.address, station.status].map(normalize).join(' ');
                if (!haystack.includes(keyword)) {
                    return false;
                }
            }

            if (location) {
                const place = [station.address, station.name].map(normalize).join(' ');
                if (!place.includes(location)) {
                    return false;
                }
            }

            return true;
        });
    }

    function applySorting(items, sortBy) {
        const cloned = Array.isArray(items) ? items.slice() : [];

        if (sortBy === 'rating') {
            return cloned.sort((a, b) => Number(b.averageRating || 0) - Number(a.averageRating || 0));
        }

        if (sortBy === 'available') {
            return cloned.sort((a, b) => Number(b.activePoleCount || b.availablePoleCount || 0) - Number(a.activePoleCount || a.availablePoleCount || 0));
        }

        if (sortBy === 'nearest' && userPosition) {
            return cloned.sort((a, b) => calcDistanceKm(userPosition, a) - calcDistanceKm(userPosition, b));
        }

        if (sortBy === 'smart' && userPosition) {
            return cloned.sort((a, b) => {
                const da = calcDistanceKm(userPosition, a);
                const db = calcDistanceKm(userPosition, b);
                const ra = Number(a.averageRating || 0);
                const rb = Number(b.averageRating || 0);
                return (da - db) || (rb - ra) || String(a.name || '').localeCompare(String(b.name || ''), 'vi');
            });
        }

        return cloned.sort((a, b) => String(a.name || '').localeCompare(String(b.name || ''), 'vi'));
    }

    function renderStationsPage(items, meta) {
        const list = document.getElementById('stationList');
        const pageBadge = document.getElementById('pageBadge');
        if (pageBadge) {
            pageBadge.textContent = `Trang ${meta.page}/${Math.max(meta.totalPages, 1)}`;
        }

        if (!list) {
            return;
        }

        if (!items || items.length === 0) {
            list.innerHTML = '<div class="station-empty" style="grid-column:1/-1;">Không có trạm nào phù hợp với bộ lọc hiện tại.</div>';
            return;
        }

        list.innerHTML = items.map(station => {
            const statusClass = normalize(station.status) === normalize('Hoạt động')
                ? 'station-status-pill station-status-active'
                : 'station-status-pill station-status-inactive';

            const distanceKm = userPosition ? calcDistanceKm(userPosition, station) : null;
            const distanceHtml = Number.isFinite(distanceKm)
                ? `<div class="station-distance"><i class="fa-solid fa-location-crosshairs"></i>Cách bạn ${distanceKm.toFixed(1)} km</div>`
                : '';

            return `
                <article class="station-item ${selectedStationId === station.id ? 'active' : ''}" data-id="${station.id}">
                    <div class="station-item-head">
                        <div>
                            <h3 class="station-item-title">${escapeHtml(station.name || 'Trạm sạc')}</h3>
                        </div>
                        <span class="${statusClass}">${escapeHtml(station.status || '-')}</span>
                    </div>

                    <div class="station-address">${escapeHtml(station.address || '-')}</div>
                    ${distanceHtml}

                    <div class="station-meta-grid">
                        <div class="station-meta-box">
                            <div class="station-meta-label">Tổng số trụ</div>
                            <div class="station-meta-value">${Number(station.totalPoleCount || 0)}</div>
                        </div>
                        <div class="station-meta-box">
                            <div class="station-meta-label">Trụ hoạt động</div>
                            <div class="station-meta-value">${Number(station.activePoleCount || station.availablePoleCount || 0)}</div>
                        </div>
                        <div class="station-meta-box">
                            <div class="station-meta-label">Đánh giá</div>
                            <div class="station-meta-value">${Number(station.averageRating || 0).toFixed(1)} (${Number(station.reviewCount || 0)})</div>
                        </div>
                        <div class="station-meta-box">
                            <div class="station-meta-label">Tọa độ</div>
                            <div class="station-meta-value">${Number(station.latitude || 0).toFixed(4)}, ${Number(station.longitude || 0).toFixed(4)}</div>
                        </div>
                    </div>

                    <div class="station-actions">
                        <a href="/User/Station/Details/${station.id}" class="btn btn-outline-secondary js-detail-link">Chi tiết trạm</a>
                        <button type="button" class="btn btn-success js-direction" data-id="${station.id}">Chỉ đường</button>
                        ${isAuthenticated
                    ? `<button type="button" class="btn ${station.isFavorite ? 'btn-warning' : 'btn-outline-warning'} js-favorite" data-id="${station.id}">${station.isFavorite ? 'Bỏ yêu thích' : 'Yêu thích'}</button>`
                    : ''}
                    </div>
                </article>
            `;
        }).join('');

        list.querySelectorAll('.station-item').forEach(item => {
            item.addEventListener('click', function (e) {
                const stationId = Number(item.dataset.id);

                if (e.target.closest('.js-direction')) {
                    e.stopPropagation();
                    const station = currentStations.find(x => Number(x.id) === stationId);
                    if (station) {
                        openGoogleMap(station.latitude, station.longitude);
                    }
                    return;
                }

                if (e.target.closest('.js-favorite')) {
                    e.preventDefault();
                    e.stopPropagation();
                    toggleFavorite(stationId);
                    return;
                }

                if (e.target.closest('.js-detail-link')) {
                    return;
                }

                focusStation(stationId, true);
            });
        });
    }

    function renderMarkers(stations, fitAll) {
        clearMarkers();
        const points = [];

        if (userPosition) {
            points.push(userPosition);
        }

        stations.forEach(station => {
            const lat = Number(station.latitude);
            const lng = Number(station.longitude);
            if (!Number.isFinite(lat) || !Number.isFinite(lng)) {
                return;
            }

            const position = { lat, lng };
            const marker = createPinMarker(position, station.name || 'Trạm sạc');
            if (!marker) {
                return;
            }

            const onMarkerClick = function () {
                selectedStationId = Number(station.id);
                if (stationPager && typeof stationPager.goToItem === 'function') {
                    stationPager.goToItem(x => Number(x.id) === Number(station.id));
                }
                openInfoPanel(station);
                focusMap(position, 15);
            };

            if (typeof marker.addListener === 'function') {
                marker.addListener('click', onMarkerClick);
            }

            currentMarkers.push({ id: Number(station.id), marker, position });
            points.push(position);
        });

        const mapChip = document.getElementById('mapChip');
        if (mapChip) {
            mapChip.textContent = `${stations.length} trạm trên bản đồ`;
        }

        if (fitAll && points.length > 0) {
            hasInitialFit = true;
            TramSacMap4D.fitBounds(stationMap, points, { lat: 16.5, lng: 107.5 }, points.length === 1 ? 15 : 6);
        }
    }

    function renderUserMarker(lat, lng) {
        if (userMarker) {
            TramSacMap4D.clearObject(userMarker);
        }

        userMarker = createUserDotMarker({ lat: Number(lat), lng: Number(lng) }, 'Vị trí của bạn');
    }

    function createPinMarker(position, title) {
        const svg = encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" width="34" height="46" viewBox="0 0 34 46"><path d="M17 1C8.164 1 1 8.164 1 17c0 11.6 14.025 26.04 15.617 27.65a.6.6 0 0 0 .766 0C18.975 43.04 33 28.6 33 17 33 8.164 25.836 1 17 1z" fill="#ef4444" stroke="white" stroke-width="2"/><circle cx="17" cy="17" r="6.5" fill="white"/></svg>`);
        const iconUrl = `data:image/svg+xml;charset=UTF-8,${svg}`;

        try {
            return TramSacMap4D.createMarker(stationMap, {
                position,
                title: title || '',
                icon: {
                    url: iconUrl,
                    width: 34,
                    height: 46
                },
                anchor: { x: 0.5, y: 1.0 },
                windowAnchor: { x: 0.5, y: 0.0 },
                visible: true,
                userInteractionEnabled: true,
                zIndex: 20
            });
        } catch (error) {
            console.warn('Map4D pin icon fallback:', error);
            try {
                return TramSacMap4D.createMarker(stationMap, {
                    position,
                    title: title || '',
                    visible: true,
                    userInteractionEnabled: true,
                    label: '•',
                    zIndex: 20
                });
            } catch (fallbackError) {
                console.error('Map4D marker error:', fallbackError);
                return null;
            }
        }
    }

    function createUserDotMarker(position, title) {
        const svg = encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24"><circle cx="12" cy="12" r="8" fill="#2563eb" stroke="white" stroke-width="4" /></svg>`);
        const iconUrl = `data:image/svg+xml;charset=UTF-8,${svg}`;

        try {
            return TramSacMap4D.createMarker(stationMap, {
                position,
                title: title || '',
                icon: {
                    url: iconUrl,
                    width: 24,
                    height: 24
                },
                anchor: { x: 0.5, y: 0.5 },
                visible: true,
                userInteractionEnabled: true,
                zIndex: 30
            });
        } catch (error) {
            console.warn('Map4D user dot fallback:', error);
            return TramSacMap4D.createMarker(stationMap, { position, title: title || '', visible: true, zIndex: 30 });
        }
    }

    function clearMarkers() {
        currentMarkers.forEach(item => TramSacMap4D.clearObject(item.marker));
        currentMarkers = [];
    }

    function focusStation(stationId, openPanel) {
        selectedStationId = Number(stationId);
        stationPager.goToItem(x => Number(x.id) === Number(stationId));
        const station = currentStations.find(x => Number(x.id) === Number(stationId));
        if (!station) {
            return;
        }

        focusMap({ lat: Number(station.latitude), lng: Number(station.longitude) }, 15);
        if (openPanel) {
            openInfoPanel(station);
        }
    }

    function focusLocationByRadius(position, radiusKm) {
        focusMap(position, getRadiusZoom(radiusKm));
    }

    function getRadiusZoom(radiusKm) {
        const zoomMap = {
            1: 16,
            2: 15,
            3: 14,
            4: 13,
            5: 12
        };
        return zoomMap[Number(radiusKm)] || 14;
    }

    function focusMap(position, zoom) {
        if (!stationMap || !position) {
            return;
        }

        const apply = function () {
            try {
                if (typeof stationMap.setCenter === 'function') {
                    stationMap.setCenter({ lat: Number(position.lat), lng: Number(position.lng) });
                }
                if (typeof stationMap.setZoom === 'function') {
                    stationMap.setZoom(Number(zoom));
                }
            } catch (error) {
                console.error('Map4D focus error:', error);
            }
        };

        apply();
        window.setTimeout(apply, 120);
        window.setTimeout(apply, 320);
    }

    function ensureInfoPanel() {
        if (infoPanel) {
            return infoPanel;
        }

        infoPanel = document.getElementById('stationMapInfo');
        return infoPanel;
    }

    function openInfoPanel(station) {
        const panel = ensureInfoPanel();
        if (!panel || !station) {
            return;
        }

        const rating = Number(station.averageRating || 0).toFixed(1);
        const reviewCount = Number(station.reviewCount || 0);
        const totalPoleCount = Number(station.totalPoleCount || 0);
        const activePoleCount = Number(station.activePoleCount || station.availablePoleCount || 0);
        const statusClass = normalize(station.status) === normalize('Hoạt động') ? '#177b46' : '#b54708';
        const distanceKm = userPosition ? calcDistanceKm(userPosition, station) : null;
        const distanceHtml = Number.isFinite(distanceKm)
            ? `<div style="font-size:13px;color:#5d6e67;margin-top:6px;">Cách bạn khoảng <strong>${distanceKm.toFixed(1)} km</strong></div>`
            : '';

        panel.innerHTML = `
            <div class="station-map-popup-arrow"></div>
            <div style="display:flex;justify-content:space-between;gap:12px;align-items:flex-start;position:relative;">
                <div>
                    <div style="font-size:20px;font-weight:800;color:#18231f;line-height:1.25;">${escapeHtml(station.name || 'Trạm sạc')}</div>
                    <div style="font-size:14px;color:#5d6e67;margin-top:6px;line-height:1.45;">${escapeHtml(station.address || '-')}</div>
                    <div style="font-size:13px;color:#6b7f77;margin-top:6px;">Loại sạc: <strong>${escapeHtml(station.chargerType || 'Đang cập nhật')}</strong> · Công suất: <strong>${escapeHtml(station.power || station.maxPower || 'Đang cập nhật')}</strong></div>
                    ${distanceHtml}
                </div>
                <button type="button" id="stationMapInfoClose" style="border:none;background:#f3f7f5;color:#18231f;width:32px;height:32px;border-radius:999px;font-weight:800;cursor:pointer;">×</button>
            </div>

            <div style="display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px;margin-top:14px;">
                <div style="padding:10px 12px;border-radius:14px;background:#f8fbfa;border:1px solid #e4eeea;">
                    <div style="font-size:12px;color:#70817a;">Trạng thái</div>
                    <div style="font-size:15px;font-weight:800;color:${statusClass};">${escapeHtml(station.status || '-')}</div>
                </div>
                <div style="padding:10px 12px;border-radius:14px;background:#f8fbfa;border:1px solid #e4eeea;">
                    <div style="font-size:12px;color:#70817a;">Đánh giá</div>
                    <div style="font-size:15px;font-weight:800;color:#18231f;">${rating} (${reviewCount})</div>
                </div>
                <div style="padding:10px 12px;border-radius:14px;background:#f8fbfa;border:1px solid #e4eeea;">
                    <div style="font-size:12px;color:#70817a;">Tổng số trụ</div>
                    <div style="font-size:15px;font-weight:800;color:#18231f;">${totalPoleCount}</div>
                </div>
                <div style="padding:10px 12px;border-radius:14px;background:#f8fbfa;border:1px solid #e4eeea;">
                    <div style="font-size:12px;color:#70817a;">Trụ hoạt động</div>
                    <div style="font-size:15px;font-weight:800;color:#18231f;">${activePoleCount}</div>
                </div>
            </div>

            <div style="display:flex;gap:10px;flex-wrap:wrap;margin-top:14px;">
                <a href="/User/Station/Details/${station.id}" style="display:inline-flex;align-items:center;justify-content:center;min-height:42px;padding:0 16px;border-radius:999px;border:1px solid #cfe3d8;background:#fff;color:#177b46;font-weight:800;text-decoration:none;">Chi tiết trạm</a>
                <button type="button" id="stationMapDirectionBtn" style="display:inline-flex;align-items:center;justify-content:center;min-height:42px;padding:0 16px;border-radius:999px;border:none;background:#22b463;color:#fff;font-weight:800;cursor:pointer;">Chỉ đường</button>
            </div>
        `;

        panel.classList.remove('is-hidden');

        panel.querySelector('#stationMapInfoClose')?.addEventListener('click', hideInfoPanel);
        panel.querySelector('#stationMapDirectionBtn')?.addEventListener('click', function () {
            openGoogleMap(station.latitude, station.longitude);
        });
    }

    function hideInfoPanel() {
        const panel = ensureInfoPanel();
        if (!panel) {
            return;
        }
        panel.classList.add('is-hidden');
        panel.innerHTML = '';
    }

    function updateKpis(stations) {
        setText('kpiTotalStations', Number(stations.length || 0));
        setText('kpiActiveStations', stations.filter(x => normalize(x.status) === normalize('Hoạt động')).length);
        setText('kpiActivePoles', stations.reduce((sum, x) => sum + Number(x.totalPoleCount || 0), 0));
        setText('kpiAvailablePoles', stations.reduce((sum, x) => sum + Number(x.activePoleCount || x.availablePoleCount || 0), 0));
    }

    function updateResultUI(count, hint) {
        setText('resultBadge', `${count} kết quả`);
        setText('mapChip', `${count} trạm trên bản đồ`);
        setText('mapHint', hint);
    }

    function setLoadingState(message) {
        const list = document.getElementById('stationList');
        if (list) {
            list.innerHTML = `<div class="station-loading">${escapeHtml(message)}</div>`;
        }
    }

    function setErrorState(message) {
        const list = document.getElementById('stationList');
        if (list) {
            list.innerHTML = `<div class="station-empty" style="grid-column:1/-1;">${escapeHtml(message)}</div>`;
        }
        updateResultUI(0, message);
        updateKpis([]);
        clearMarkers();
        hideInfoPanel();
    }

    function resetFilters() {
        const keyword = document.getElementById('keyword');
        const location = document.getElementById('location');
        const status = document.getElementById('status');
        const sortBy = document.getElementById('sortBy');
        const radius = document.getElementById('radiusKm');
        if (keyword) keyword.value = '';
        if (location) location.value = '';
        if (status) status.value = '';
        if (sortBy) sortBy.value = 'smart';
        if (radius) radius.value = '3';
        const note = document.getElementById('locationNote');
        if (note) {
            note.innerHTML = 'Chưa dùng vị trí hiện tại. Khi bấm <strong>Gần tôi</strong>, bản đồ sẽ chỉ zoom gần quanh vị trí của bạn theo bán kính đã chọn, nhưng các trạm khác vẫn còn trên map để bạn zoom ra xem tiếp.';
        }
        closeKeywordSuggest();
        hideInfoPanel();
    }

    function queueKeywordSuggest(immediate) {
        clearTimeout(keywordSuggestTimer);
        const keyword = document.getElementById('keyword')?.value.trim() || '';
        if (keyword.length < 2) {
            closeKeywordSuggest();
            return;
        }

        keywordSuggestTimer = window.setTimeout(function () {
            fetchKeywordSuggestions(keyword);
        }, immediate ? 0 : 220);
    }

    async function fetchKeywordSuggestions(keyword) {
        try {
            if (keywordSuggestAbortController) {
                keywordSuggestAbortController.abort();
            }

            keywordSuggestAbortController = new AbortController();
            const response = await fetch(`/User/api/stations/suggestions?keyword=${encodeURIComponent(keyword)}`, {
                signal: keywordSuggestAbortController.signal
            });

            if (!response.ok) {
                throw new Error('Không tải được gợi ý trạm.');
            }

            keywordSuggestItems = await response.json();
            keywordSuggestIndex = -1;
            renderKeywordSuggestions('Không tìm thấy trạm phù hợp với từ khóa này.');
        } catch (error) {
            if (error.name === 'AbortError') {
                return;
            }
            console.error(error);
            keywordSuggestItems = [];
            renderKeywordSuggestions('Không lấy được gợi ý tìm kiếm.');
        }
    }

    function renderKeywordSuggestions(fallbackMessage) {
        const box = document.getElementById('keywordSuggestBox');
        if (!box) {
            return;
        }

        if (!keywordSuggestItems.length) {
            if (fallbackMessage) {
                box.innerHTML = `<div class="station-suggest-empty">${escapeHtml(fallbackMessage)}</div>`;
                box.classList.add('is-open');
            } else {
                closeKeywordSuggest();
            }
            return;
        }

        box.innerHTML = keywordSuggestItems.map((item, index) => `
            <button type="button" class="station-suggest-item ${index === keywordSuggestIndex ? 'is-active' : ''}" data-id="${item.id}">
                <div class="station-suggest-title-row">
                    <div class="station-suggest-title">${escapeHtml(item.name || 'Trạm sạc')}</div>
                    <span class="station-suggest-badge">${escapeHtml(item.badge || 'Trạm sạc')}</span>
                </div>
                <div class="station-suggest-address">${escapeHtml(item.address || '')}</div>
                <div class="station-suggest-meta">${escapeHtml(item.subtitle || 'Chọn để lọc nhanh đúng trạm')}</div>
            </button>
        `).join('');
        box.classList.add('is-open');

        box.querySelectorAll('.station-suggest-item').forEach(button => {
            button.addEventListener('mousedown', function (e) {
                e.preventDefault();
                selectKeywordSuggestion(Number(button.dataset.id));
            });
        });
    }

    function handleKeywordSuggestKeydown(e) {
        const box = document.getElementById('keywordSuggestBox');
        const isOpen = box?.classList.contains('is-open');
        if (!isOpen || !keywordSuggestItems.length) {
            return false;
        }

        if (e.key === 'ArrowDown') {
            e.preventDefault();
            keywordSuggestIndex = Math.min(keywordSuggestIndex + 1, keywordSuggestItems.length - 1);
            renderKeywordSuggestions();
            return true;
        }

        if (e.key === 'ArrowUp') {
            e.preventDefault();
            keywordSuggestIndex = Math.max(keywordSuggestIndex - 1, 0);
            renderKeywordSuggestions();
            return true;
        }

        if (e.key === 'Escape') {
            e.preventDefault();
            closeKeywordSuggest();
            return true;
        }

        if (e.key === 'Enter' && keywordSuggestIndex >= 0 && keywordSuggestItems[keywordSuggestIndex]) {
            e.preventDefault();
            selectKeywordSuggestion(Number(keywordSuggestItems[keywordSuggestIndex].id));
            return true;
        }

        return false;
    }

    function selectKeywordSuggestion(stationId) {
        const item = keywordSuggestItems.find(x => Number(x.id) === Number(stationId));
        if (!item) {
            return;
        }

        const keyword = document.getElementById('keyword');
        if (keyword) {
            keyword.value = item.name || '';
        }

        selectedStationId = Number(item.id);
        closeKeywordSuggest();
        applyFilters({ focusStationId: Number(item.id) });
    }

    function closeKeywordSuggest() {
        const box = document.getElementById('keywordSuggestBox');
        if (!box) {
            return;
        }
        box.classList.remove('is-open');
        box.innerHTML = '';
        keywordSuggestItems = [];
        keywordSuggestIndex = -1;
    }

    async function toggleFavorite(stationId) {
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (!token) {
            alert('Thiếu anti-forgery token.');
            return;
        }

        try {
            const response = await fetch(`/User/api/favorites/${stationId}/toggle`, {
                method: 'POST',
                headers: {
                    'RequestVerificationToken': token
                }
            });

            const result = await response.json();
            if (!response.ok || !result.success) {
                alert(result.message || 'Không thể cập nhật yêu thích.');
                return;
            }

            const updateItem = function (items) {
                const target = items.find(x => Number(x.id) === Number(stationId));
                if (target) {
                    target.isFavorite = result.isFavorite;
                }
            };

            updateItem(allStations);
            updateItem(currentStations);
            stationPager.setItems(currentStations, true);
        } catch (error) {
            console.error(error);
            alert('Có lỗi xảy ra khi cập nhật yêu thích.');
        }
    }

    function getCurrentPosition() {
        return new Promise((resolve, reject) => {
            navigator.geolocation.getCurrentPosition(resolve, reject, {
                enableHighAccuracy: true,
                timeout: 10000,
                maximumAge: 0
            });
        });
    }

    function buildMapHint(filters) {
        if (filters.keyword || filters.location || filters.status) {
            return 'Đang hiển thị kết quả theo bộ lọc hiện tại.';
        }
        return 'Đang hiển thị toàn bộ trạm trong hệ thống.';
    }

    function calcDistanceKm(origin, station) {
        const lat1 = Number(origin.lat || 0) * Math.PI / 180;
        const lng1 = Number(origin.lng || 0) * Math.PI / 180;
        const lat2 = Number(station.latitude || station.lat || 0) * Math.PI / 180;
        const lng2 = Number(station.longitude || station.lng || 0) * Math.PI / 180;
        const dLat = lat2 - lat1;
        const dLng = lng2 - lng1;
        const a = Math.sin(dLat / 2) ** 2 + Math.cos(lat1) * Math.cos(lat2) * Math.sin(dLng / 2) ** 2;
        const c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
        return 6371 * c;
    }

    function normalize(value) {
        return String(value || '')
            .normalize('NFD')
            .replace(/\p{Diacritic}/gu, '')
            .toLowerCase()
            .trim();
    }

    function openGoogleMap(lat, lng) {
        window.open(`https://www.google.com/maps/dir/?api=1&destination=${lat},${lng}`, '_blank');
    }

    function setText(id, value) {
        const el = document.getElementById(id);
        if (el) {
            el.textContent = value;
        }
    }

    function escapeHtml(text) {
        return String(text || '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#039;');
    }

    function wait(ms) {
        return new Promise(resolve => window.setTimeout(resolve, ms));
    }
})();
