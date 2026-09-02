namespace Matraca.Mac.Platform.Interop;

internal sealed class ObjCClassBuilder
{
    private readonly string _name;
    private readonly IntPtr _class;
    private bool _registered;

    private ObjCClassBuilder(string name, IntPtr cls)
    {
        _name = name;
        _class = cls;
    }

    public static ObjCClassBuilder Create(string name, IntPtr superclass)
    {
        IntPtr existing = ObjC.objc_getClass(name);
        if (existing != IntPtr.Zero)
            return new ObjCClassBuilder(name, existing) { _registered = true };

        IntPtr cls = ObjC.objc_allocateClassPair(superclass, name, 0);
        if (cls == IntPtr.Zero)
            throw new InvalidOperationException($"Could not allocate Objective-C class {name}.");
        return new ObjCClassBuilder(name, cls);
    }

    public ObjCClassBuilder AddMethod(IntPtr selector, IntPtr implementation, string types)
    {
        if (!_registered && !ObjC.class_addMethod(_class, selector, implementation, types))
            throw new InvalidOperationException($"Could not add method to Objective-C class {_name}.");
        return this;
    }

    public IntPtr Register()
    {
        if (!_registered)
        {
            ObjC.objc_registerClassPair(_class);
            _registered = true;
        }
        return _class;
    }
}
