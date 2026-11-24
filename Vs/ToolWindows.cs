using System.Runtime.InteropServices;

namespace DteMcp.Vs
{
    /// <summary>
    /// Reaches the Error List and the Output window. They hang off <c>EnvDTE80.DTE2.ToolWindows</c>, which the late-bound
    /// <c>EnvDTE.DTE</c> object read from the Running Object Table does not expose (neither by name nor by DISPID), and
    /// <c>Windows.Item(guid).Object</c> cannot give the Error List's items. So the two interfaces are declared here, with
    /// the vtable layout of EnvDTE80, and the results are used late bound.
    /// </summary>
    internal static class ToolWindows
    {
        /// <summary>The Error List's items (<c>EnvDTE80.ErrorItems</c>). Item numbers start at 1.</summary>
        public static IErrorItems ErrorItems(object dte)
        {
            var errorList = (IErrorList)((IToolWindows)((IDte2)dte).GetToolWindows()).GetErrorList();
            return (IErrorItems)errorList.GetErrorItems();
        }

        /// <summary>The Output window (<c>EnvDTE.OutputWindow</c>), for late-bound use.</summary>
        public static object OutputWindow(object dte)
        {
            return ((IToolWindows)((IDte2)dte).GetToolWindows()).GetOutputWindow();
        }

        /// <summary>EnvDTE80.DTE2. Only the slot of ToolWindows is used; the 49 before it keep its place in the vtable.</summary>
        [ComImport, Guid("2ee1e9fa-0afe-4348-a89f-ed9cb45c99cf"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface IDte2
        {
            void Slot00(); void Slot01(); void Slot02(); void Slot03(); void Slot04(); void Slot05(); void Slot06();
            void Slot07(); void Slot08(); void Slot09(); void Slot10(); void Slot11(); void Slot12(); void Slot13();
            void Slot14(); void Slot15(); void Slot16(); void Slot17(); void Slot18(); void Slot19(); void Slot20();
            void Slot21(); void Slot22(); void Slot23(); void Slot24(); void Slot25(); void Slot26(); void Slot27();
            void Slot28(); void Slot29(); void Slot30(); void Slot31(); void Slot32(); void Slot33(); void Slot34();
            void Slot35(); void Slot36(); void Slot37(); void Slot38(); void Slot39(); void Slot40(); void Slot41();
            void Slot42(); void Slot43(); void Slot44(); void Slot45(); void Slot46(); void Slot47(); void Slot48();

            [return: MarshalAs(UnmanagedType.IDispatch)]
            object GetToolWindows();
        }

        /// <summary>EnvDTE80.ToolWindows. Only OutputWindow (slot 4) and ErrorList (slot 7) are used.</summary>
        [ComImport, Guid("19ac6f68-3019-4d65-8d98-404dfb96b8e2"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface IToolWindows
        {
            void Slot0(); void Slot1(); void Slot2(); void Slot3();

            [return: MarshalAs(UnmanagedType.IDispatch)]
            object GetOutputWindow();

            void Slot5(); void Slot6();

            [return: MarshalAs(UnmanagedType.IDispatch)]
            object GetErrorList();
        }

        /// <summary>EnvDTE80.ErrorList. Only ErrorItems (slot 8) is used.</summary>
        [ComImport, Guid("7bf84ef1-8246-498a-a127-2ea37824fda1"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface IErrorList
        {
            void Slot0(); void Slot1(); void Slot2(); void Slot3(); void Slot4(); void Slot5(); void Slot6(); void Slot7();

            [return: MarshalAs(UnmanagedType.IDispatch)]
            object GetErrorItems();
        }

        /// <summary>EnvDTE80.ErrorItems.</summary>
        [ComImport, Guid("dcf93a30-d013-42f4-8aee-9f5ba215fb8b"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        public interface IErrorItems
        {
            void Slot0(); void Slot1();

            [return: MarshalAs(UnmanagedType.IDispatch)]
            object GetItem(object index);

            int GetCount();
        }

        /// <summary>EnvDTE80.ErrorItem. ErrorLevel is 4 for an error, 2 for a warning, 1 for a message (vsBuildErrorLevel).</summary>
        [ComImport, Guid("f49a191f-7258-493b-9310-5f7771ddf3d7"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        public interface IErrorItem
        {
            void Slot0(); void Slot1();

            int GetErrorLevel();

            [return: MarshalAs(UnmanagedType.BStr)]
            string GetDescription();

            [return: MarshalAs(UnmanagedType.BStr)]
            string GetFileName();

            int GetLine();

            int GetColumn();

            [return: MarshalAs(UnmanagedType.BStr)]
            string GetProject();
        }
    }
}
