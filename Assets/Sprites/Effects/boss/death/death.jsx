// death — 由 gen_all_boss_fx.py 自动生成
// 用法: After Effects > 文件 > 脚本 > 运行脚本文件... 选择本文件
(function() {
    if (typeof app === "undefined") { alert("请在 After Effects 中运行此脚本"); return; }
    var framesDir = "C:\\Users\\26853\\Documents\\VampireBossFX\\death\\frames";
    var compName  = "death";
    var fps       = 24;
    var numFrames = 20;
    var proj = app.project;
    var dir = new Folder(framesDir);
    if (!dir.exists) { alert("找不到帧目录:\n" + framesDir); return; }
    var files = dir.getFiles(/\.png$/i);
    if (files.length === 0) { alert("目录内没有 PNG 帧: " + framesDir); return; }
    files.sort();
    var io = new ImportOptions(files[0]);
    io.sequence = true;
    io.forceAlphabetical = true;
    var footage = proj.importFile(io);
    if (footage && footage.mainSource) footage.mainSource.conformFrameRate = fps;
    var dur = numFrames / fps;
    var comp = proj.items.addComp(compName, 1024, 1024, 1, dur, fps);
    var layer = comp.layers.add(footage);
    layer.startTime = 0;
    alert("已创建合成 '" + compName + "': " + numFrames + " 帧 @ " + fps + " fps");
})();
